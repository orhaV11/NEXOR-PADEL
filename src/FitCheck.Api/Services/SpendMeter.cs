using System.Globalization;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>
/// What one model call cost in tokens, as the Messages API's <c>usage</c> block reported it. Cache fields are 0 unless
/// prompt caching is on (this app does not use it today, so they are 0 in practice and are read anyway, so the numbers
/// stay honest the day someone turns it on).
/// </summary>
public readonly record struct VisionUsage(long InputTokens, long OutputTokens, long CacheReadTokens = 0, long CacheWriteTokens = 0)
{
    /// <summary>A call whose token counts are not known: a timeout, or an error body that carried no usage.</summary>
    public static readonly VisionUsage Unknown = default;

    public bool Any => InputTokens > 0 || OutputTokens > 0 || CacheReadTokens > 0 || CacheWriteTokens > 0;

    /// <summary>
    /// The usage out of a Messages API response body: <c>usage.input_tokens</c>, <c>usage.output_tokens</c>,
    /// <c>usage.cache_read_input_tokens</c> and <c>usage.cache_creation_input_tokens</c>. Anything missing,
    /// non-numeric or negative reads as 0, so a shape we do not know cannot make a number up.
    /// </summary>
    public static VisionUsage Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("usage", out var usage)
            || usage.ValueKind != JsonValueKind.Object)
        {
            return Unknown;
        }

        return new VisionUsage(
            Number(usage, "input_tokens"),
            Number(usage, "output_tokens"),
            Number(usage, "cache_read_input_tokens"),
            Number(usage, "cache_creation_input_tokens"));
    }

    /// <summary>The same, from a body that may not be JSON at all (an error page, a proxy's HTML): unreadable is Unknown.</summary>
    public static VisionUsage ReadFrom(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            return Read(doc.RootElement);
        }
        catch (JsonException)
        {
            return Unknown;
        }
    }

    private static long Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number > 0
            ? number
            : 0;
}

/// <summary>Today's meter as the numbers page and the gate read it.</summary>
public sealed record SpendDay(string Day, long Calls, long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, decimal EstimatedUsd);

/// <summary>
/// The spend meter: what the stylist has cost today, and the ceiling that stops it costing more.
/// <list type="bullet">
/// <item><b>The rows.</b> Every model call that reached the API adds to four <see cref="Counter"/> rows for its UTC day:
/// <c>spend:calls:yyyyMMdd</c>, <c>spend:in:yyyyMMdd</c>, <c>spend:out:yyyyMMdd</c> and, when the API reports them,
/// <c>spend:cache_read:yyyyMMdd</c> and <c>spend:cache_write:yyyyMMdd</c>. A call that failed after the API had already
/// billed it (a 4xx whose body still carried usage, a timeout) counts what is known — the call always, the tokens when
/// there are any. A call that never reached the API (no connection, no API key) counts nothing: nobody billed it.</item>
/// <item><b>The estimate.</b> <c>Anthropic:PriceInPerMillion</c> and <c>Anthropic:PriceOutPerMillion</c>, in USD, give
/// an ESTIMATE and nothing more: they are settings an owner must match to their own contract, and a call's real price
/// depends on the model, the tier and the month's invoice. Cache tokens are priced at the input price, which OVERSTATES
/// cache reads (Anthropic bills them at a fraction of input) — deliberately, because a ceiling that guesses low is a
/// ceiling that lets a real bill through. The app uses no prompt caching today, so both are 0.</item>
/// <item><b>The ceiling.</b> <c>Limits:SpendPerDayUsd</c> (0 = off). Once today's estimate reaches it,
/// <see cref="CeilingReachedAsync"/> is true and every route that would ask the model answers 503
/// error.stylist_resting before making the call — no allowance spent, no guest's free look spent, no row stored. It
/// opens again at the next UTC midnight, because the rows are per UTC day. One log line and one alert when it first
/// closes on a given day, never one per refused request.</item>
/// </list>
/// The clock is <see cref="IClock"/>, so a test can walk over midnight without waiting.
/// </summary>
public sealed class SpendMeter(
    IServiceScopeFactory scopes,
    IClock clock,
    IOptions<AnthropicOptions> anthropic,
    IOptions<LimitsOptions> limits,
    Alerter alerter,
    ILogger<SpendMeter> logger)
{
    public const string CallsPrefix = "spend:calls:";
    public const string InPrefix = "spend:in:";
    public const string OutPrefix = "spend:out:";
    public const string CacheReadPrefix = "spend:cache_read:";
    public const string CacheWritePrefix = "spend:cache_write:";

    /// <summary>How many days the numbers page's series covers.</summary>
    public const int SeriesDays = 14;

    private const string DayFormat = "yyyyMMdd";
    private const long Million = 1_000_000;

    /// <summary>The day whose closing was already said out loud, so the log and the alert happen once a day, not once a request.</summary>
    private string _announcedDay = "";
    private readonly object _announce = new();

    /// <summary>The UTC day a counter row is named after.</summary>
    public static string DayKey(DateTime utc) => utc.ToString(DayFormat, CultureInfo.InvariantCulture);

    /// <summary>Today, by the app's clock.</summary>
    public string Today => DayKey(clock.UtcNow);

    /// <summary>The ceiling in USD; 0 means there is none.</summary>
    public decimal CeilingUsd => limits.Value.SpendPerDayUsd;

    /// <summary>The prices in use, as the doctor and the numbers page print them.</summary>
    public (decimal In, decimal Out) Prices => (anthropic.Value.PriceInPerMillion, anthropic.Value.PriceOutPerMillion);

    /// <summary>
    /// Records one model call the API answered. Opens its own scope, so nothing it writes can join, or fail, the
    /// request's own transaction. Never throws: a check that already happened must not fail because a tally did.
    /// </summary>
    public Task RecordAsync(VisionUsage usage, CancellationToken ct = default) => RecordAsync(usage, failed: false, ct);

    /// <summary>
    /// Records a call that reached the API and failed there (a 4xx or 5xx, or a timeout): the call is counted, the
    /// tokens only if the answer carried any, and the failure is offered to the alerter's ten-minute window.
    /// </summary>
    public async Task RecordFailedAsync(VisionUsage usage, CancellationToken ct = default)
    {
        await RecordAsync(usage, failed: true, ct);
        try
        {
            await alerter.ModelFailedAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "The model-failure alert could not be raised.");
        }
    }

    /// <summary>
    /// Round 17. The model could not be reached, or has no key: alert, and record NOTHING. A call that never left the
    /// machine was never billed, so counting it would make the spend meter lie about a day it did not spend. The
    /// alerter's own per-kind quiet hour is the throttle; this deliberately does not go through the ten-minute
    /// failure window, which counts calls and would have stayed at zero through exactly this outage.
    /// </summary>
    public Task ModelUnreachableAsync(string detail, CancellationToken ct = default) =>
        SafelyAsync(() => alerter.ModelUnreachableAsync(detail, ct), "The model-unreachable alert could not be raised.");

    public Task ModelKeyMissingAsync(CancellationToken ct = default) =>
        SafelyAsync(() => alerter.ModelKeyMissingAsync(ct), "The missing-key alert could not be raised.");

    /// <summary>An alert must never be the reason a check fails differently than it would have failed anyway.</summary>
    private async Task SafelyAsync(Func<Task> raise, string whenItFails)
    {
        try
        {
            await raise();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "{Message}", whenItFails);
        }
    }

    private async Task RecordAsync(VisionUsage usage, bool failed, CancellationToken ct)
    {
        var day = Today;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await Counters.IncrementAsync(db, CallsPrefix + day, ct);
            if (usage.InputTokens > 0)
            {
                await Counters.IncrementAsync(db, InPrefix + day, ct, usage.InputTokens);
            }

            if (usage.OutputTokens > 0)
            {
                await Counters.IncrementAsync(db, OutPrefix + day, ct, usage.OutputTokens);
            }

            if (usage.CacheReadTokens > 0)
            {
                await Counters.IncrementAsync(db, CacheReadPrefix + day, ct, usage.CacheReadTokens);
            }

            if (usage.CacheWriteTokens > 0)
            {
                await Counters.IncrementAsync(db, CacheWritePrefix + day, ct, usage.CacheWriteTokens);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "The spend meter could not record a {Kind} model call for {Day}.", failed ? "failed" : "completed", day);
        }
    }

    /// <summary>What the four (five) rows for a day say, priced.</summary>
    public async Task<SpendDay> DayAsync(AppDbContext db, string day, CancellationToken ct)
    {
        var names = new[] { CallsPrefix + day, InPrefix + day, OutPrefix + day, CacheReadPrefix + day, CacheWritePrefix + day };
        var rows = await db.Counters.Where(c => names.Contains(c.Name)).ToDictionaryAsync(c => c.Name, c => c.Value, StringComparer.Ordinal, ct);
        long Read(string prefix) => rows.TryGetValue(prefix + day, out var value) ? value : 0;
        var input = Read(InPrefix);
        var output = Read(OutPrefix);
        var cacheRead = Read(CacheReadPrefix);
        var cacheWrite = Read(CacheWritePrefix);
        return new SpendDay(day, Read(CallsPrefix), input, output, cacheRead, cacheWrite, Estimate(input, output, cacheRead, cacheWrite));
    }

    /// <summary>Today.</summary>
    public Task<SpendDay> TodayAsync(AppDbContext db, CancellationToken ct) => DayAsync(db, Today, ct);

    /// <summary>The last <paramref name="days"/> UTC days, oldest first, days with nothing on them included as zeroes.</summary>
    public async Task<List<SpendDay>> SeriesAsync(AppDbContext db, CancellationToken ct, int days = SeriesDays)
    {
        var count = Math.Clamp(days, 1, 92);
        var today = clock.UtcNow.Date;
        var wanted = Enumerable.Range(0, count).Select(back => DayKey(today.AddDays(-(count - 1 - back)))).ToList();
        var names = wanted
            .SelectMany(day => new[] { CallsPrefix + day, InPrefix + day, OutPrefix + day, CacheReadPrefix + day, CacheWritePrefix + day })
            .ToList();
        var rows = await db.Counters.Where(c => names.Contains(c.Name)).ToDictionaryAsync(c => c.Name, c => c.Value, StringComparer.Ordinal, ct);

        var series = new List<SpendDay>(count);
        foreach (var day in wanted)
        {
            long Read(string prefix) => rows.TryGetValue(prefix + day, out var value) ? value : 0;
            var input = Read(InPrefix);
            var output = Read(OutPrefix);
            var cacheRead = Read(CacheReadPrefix);
            var cacheWrite = Read(CacheWritePrefix);
            series.Add(new SpendDay(day, Read(CallsPrefix), input, output, cacheRead, cacheWrite, Estimate(input, output, cacheRead, cacheWrite)));
        }

        return series;
    }

    /// <summary>
    /// The estimated dollars for a day's tokens, rounded to the cent it will be shown in. Input, cache writes and cache
    /// reads are all priced at the input price (see the class comment: an overstatement on cache reads, on purpose).
    /// </summary>
    public decimal Estimate(long input, long output, long cacheRead = 0, long cacheWrite = 0)
    {
        var prices = Prices;
        var dollars = ((decimal)(input + cacheRead + cacheWrite) * prices.In + (decimal)output * prices.Out) / Million;
        return Math.Round(dollars, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Whether the stylist is resting: <c>Limits:SpendPerDayUsd</c> is set and today's estimate has reached it. The
    /// first time it is true on a given day this writes one log line and raises one alert; after that it is silent,
    /// because every refused request would otherwise write another.
    /// </summary>
    public async Task<bool> CeilingReachedAsync(AppDbContext db, CancellationToken ct)
    {
        var ceiling = CeilingUsd;
        if (ceiling <= 0)
        {
            return false;
        }

        var today = await TodayAsync(db, ct);
        if (today.EstimatedUsd < ceiling)
        {
            return false;
        }

        Announce(today, ceiling);
        return true;
    }

    /// <summary>One line and one alert per day, whatever the traffic. The alerter has its own hour-long throttle on top.</summary>
    private void Announce(SpendDay today, decimal ceiling)
    {
        lock (_announce)
        {
            if (_announcedDay == today.Day)
            {
                return;
            }

            _announcedDay = today.Day;
        }

        logger.LogWarning(
            "The daily spend ceiling is reached: {Day} is estimated at {Estimate} USD over {Calls} model calls, and Limits:SpendPerDayUsd is {Ceiling}. Checks, comparisons and the stylist answer 503 until the next UTC midnight.",
            today.Day, today.EstimatedUsd.ToString("0.00##", CultureInfo.InvariantCulture), today.Calls,
            ceiling.ToString("0.00##", CultureInfo.InvariantCulture));

        alerter.Raise(Alerter.Kind.SpendCeiling,
            $"the daily spend ceiling is reached: {today.EstimatedUsd.ToString("0.00##", CultureInfo.InvariantCulture)} USD estimated over {today.Calls.ToString(CultureInfo.InvariantCulture)} model calls, and Limits:SpendPerDayUsd is {ceiling.ToString("0.00##", CultureInfo.InvariantCulture)}. The stylist is resting until the next UTC midnight.");
    }
}
