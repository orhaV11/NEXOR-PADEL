using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using FitCheck.Api.Domain;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>
/// Round 19 — the day's weather for the Tomorrow screen, from Open-Meteo, fetched by the server so the browser never
/// talks to a third party (the CSP's connect-src stays 'self'). One call gives three days (today, tomorrow, the day
/// after) for one place; the answer is cached by place and kept for Weather:CacheMinutes, so a neighbourhood asking
/// all morning costs one request.
/// <para>
/// What travels: latitude and longitude rounded to two decimals (about a kilometre — a forecast needs no more, and the
/// client rounds before sending too), and nothing else. What is kept: the forecast, in memory, never the place on a
/// row and never in a log line. What comes back is <see cref="Forecast"/>: a high, a low, a chance of rain and a sky
/// word from a fixed table over the WMO code — the numbers are the model's input, in words this file chose.
/// </para>
/// <para>
/// Never throws to a caller: a slow or refused forecast is null, and the outfit is composed without a weather line.
/// The wait is bounded by the named client's timeout (Weather:TimeoutSeconds).
/// </para>
/// </summary>
public sealed class Weather(IHttpClientFactory http, IOptions<WeatherOptions> options, IClock clock, ILogger<Weather> logger)
{
    public const string HttpClientName = "weather";

    /// <summary>Today, tomorrow and the day after: what the screen offers, and what one request fetches.</summary>
    public const int ForecastDays = 3;

    /// <summary>A cache beyond this many places is emptied whole rather than grown; the next askers refill it.</summary>
    public const int MaxCachedPlaces = 1000;

    /// <summary>A forecast that could not be fetched is remembered this long, not <see cref="WeatherOptions.CacheMinutes"/>: a blip must not blank an hour.</summary>
    public static readonly TimeSpan FailureMemory = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<(double Lat, double Lon), (DateTime Until, IReadOnlyDictionary<DateOnly, Forecast>? Days)> _cache = new();

    public bool Enabled => options.Value.Enabled;

    /// <summary>
    /// The forecast for <paramref name="day"/> at the place, or null: the feature is off, the place is not on Earth,
    /// the day is not one of the next three, or Open-Meteo did not answer in time or in shape.
    /// </summary>
    public async Task<Forecast?> ForAsync(double latitude, double longitude, DateOnly day, CancellationToken ct)
    {
        if (!Enabled || !OnEarth(latitude, longitude))
        {
            return null;
        }

        var lat = Math.Round(latitude, 2, MidpointRounding.AwayFromZero);
        var lon = Math.Round(longitude, 2, MidpointRounding.AwayFromZero);
        var cell = (Math.Round(lat, 1), Math.Round(lon, 1));
        var now = clock.UtcNow;
        if (_cache.TryGetValue(cell, out var cached) && cached.Until > now)
        {
            return cached.Days?.GetValueOrDefault(day);
        }

        var days = await FetchAsync(lat, lon, ct);
        if (_cache.Count >= MaxCachedPlaces)
        {
            _cache.Clear();
        }

        // A failure is remembered too, briefly, so a service that is down is not asked again by every tap for the
        // next five minutes; a success is kept for the configured hour.
        _cache[cell] = (days is null ? now + FailureMemory : now.AddMinutes(Math.Max(1, options.Value.CacheMinutes)), days);
        return days?.GetValueOrDefault(day);
    }

    /// <summary>Forgets every cached forecast. Tests, and nothing else, need this.</summary>
    public void Forget() => _cache.Clear();

    public static bool OnEarth(double latitude, double longitude) =>
        !double.IsNaN(latitude) && !double.IsNaN(longitude) && Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180;

    private async Task<IReadOnlyDictionary<DateOnly, Forecast>?> FetchAsync(double lat, double lon, CancellationToken ct)
    {
        var settings = options.Value;
        var culture = CultureInfo.InvariantCulture;
        var url = settings.Host() + "/v1/forecast"
            + "?latitude=" + lat.ToString("0.##", culture)
            + "&longitude=" + lon.ToString("0.##", culture)
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max"
            + "&forecast_days=" + ForecastDays.ToString(culture)
            + "&timezone=auto"
            + (string.IsNullOrWhiteSpace(settings.ApiKey) ? "" : "&apikey=" + Uri.EscapeDataString(settings.ApiKey.Trim()));
        try
        {
            // The wait is bounded here, by Weather:TimeoutSeconds, whatever the named client's own timeout is: the
            // person is standing there, and the outfit is worth more than the forecast.
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
            bounded.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 30)));
            using var client = http.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, bounded.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("The forecast answered {Status}; the outfit is composed without the weather.", (int)response.StatusCode);
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(bounded.Token));
            return Parse(document.RootElement);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The person went away: nothing to log, nothing to remember.
            throw;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            // The place is deliberately not in this line: a log is not where somebody's whereabouts belong.
            logger.LogWarning("The forecast could not be fetched ({Reason}); the outfit is composed without the weather.", e.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Open-Meteo's daily block: parallel arrays under "daily", one entry per day, dated in the place's own time zone
    /// (timezone=auto), which is what "tomorrow" means to the person standing there. Anything missing or misshapen is
    /// no forecast rather than a wrong one.
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, Forecast>? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("daily", out var daily) || daily.ValueKind != JsonValueKind.Object
            || !daily.TryGetProperty("time", out var time) || time.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var maxes = Numbers(daily, "temperature_2m_max");
        var mins = Numbers(daily, "temperature_2m_min");
        var rain = Numbers(daily, "precipitation_probability_max");
        var codes = Numbers(daily, "weather_code");
        var days = new Dictionary<DateOnly, Forecast>();
        var index = 0;
        foreach (var entry in time.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(entry.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && index < maxes.Count && index < mins.Count && maxes[index] is { } max && mins[index] is { } min)
            {
                var chance = index < rain.Count && rain[index] is { } p ? (int)Math.Clamp(Math.Round(p, MidpointRounding.AwayFromZero), 0, 100) : 0;
                var code = index < codes.Count && codes[index] is { } c ? (int)c : 0;
                // Half away from zero, the way a person rounds a temperature; the default rounds 20.5 to 20.
                days[day] = new Forecast(day, Math.Round(max, 1, MidpointRounding.AwayFromZero), Math.Round(min, 1, MidpointRounding.AwayFromZero), chance, code, Sky(code));
            }

            index++;
        }

        return days.Count == 0 ? null : days;
    }

    private static List<double?> Numbers(JsonElement daily, string name)
    {
        var values = new List<double?>();
        if (daily.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in array.EnumerateArray())
            {
                values.Add(value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null);
            }
        }

        return values;
    }

    /// <summary>
    /// The WMO weather code as one of seven buckets the client has a word for and the model is told in English. The
    /// table is deliberately coarse: an outfit cares whether it is wet, cold and bright, not about the difference
    /// between moderate and heavy drizzle.
    /// </summary>
    public static string Sky(int code) => code switch
    {
        0 or 1 => "clear",
        2 or 3 => "cloudy",
        45 or 48 => "fog",
        >= 51 and <= 57 => "drizzle",
        >= 61 and <= 67 => "rain",
        >= 71 and <= 77 => "snow",
        80 or 81 or 82 => "rain",
        85 or 86 => "snow",
        >= 95 => "storm",
        _ => "cloudy"
    };

    /// <summary>Every bucket <see cref="Sky"/> can answer, in one place, so the client's strings and the tests can walk them.</summary>
    public static readonly string[] Skies = ["clear", "cloudy", "fog", "drizzle", "rain", "snow", "storm"];

    /// <summary>The sky bucket as the model reads it: plain English, whatever language the person writes in.</summary>
    public static string SkyWords(string sky) => sky switch
    {
        "clear" => "clear",
        "cloudy" => "cloudy",
        "fog" => "fog",
        "drizzle" => "drizzle",
        "rain" => "rain",
        "snow" => "snow",
        "storm" => "storms",
        _ => "cloudy"
    };
}

/// <summary>
/// One day's forecast, in the numbers the model is given and the screen draws. Celsius, because the model reasons in
/// it and the client writes it in the reader's own units.
/// </summary>
public sealed record Forecast(DateOnly Day, double MaxC, double MinC, int RainChance, int Code, string Sky)
{
    /// <summary>The line in the prompt, InvariantCulture: "high 24 C, low 17 C, chance of rain 10%, clear sky".</summary>
    public string Figure()
    {
        var culture = CultureInfo.InvariantCulture;
        return "high " + Math.Round(MaxC, MidpointRounding.AwayFromZero).ToString("0", culture)
            + " C, low " + Math.Round(MinC, MidpointRounding.AwayFromZero).ToString("0", culture)
            + " C, chance of rain " + RainChance.ToString(culture) + "%, " + Weather.SkyWords(Sky);
    }
}
