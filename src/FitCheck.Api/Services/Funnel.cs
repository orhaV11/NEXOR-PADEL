using System.Globalization;
using System.Text.RegularExpressions;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>
/// Round 13 — the growth loop: the funnel, end to end, as day tallies nobody has to trust a third party for.
/// <para>
/// The steps, in the order a person walks them: a landing view, a guest check, a signup, a first post, and an arrival
/// on a look's public page (a share, when it carried <c>?via=share</c>). Three of them are <see cref="Counter"/> rows
/// this file names and <see cref="Count"/> or <see cref="Endpoints.PublicPageEndpoints"/> writes; the other three are
/// counted off the rows that already exist (checks, accounts, posts), so nothing is double-booked and a restart loses
/// nothing.
/// </para>
/// <para>
/// <see cref="Count"/> is the middleware: one tally per page view of <c>/landing/…</c> per day, and one per arrival
/// carrying an invite's <c>?via=&lt;handle&gt;</c>. It sets no cookie, reads nothing beyond the path, the query and
/// (Round 20) the one launch header on <c>/api/config</c>, stores no address and asks nothing off this machine: a day's
/// number, and nothing that could name a person. It runs before the static files, since a landing page is a static
/// file and would otherwise never reach a handler.
/// </para>
/// <para>
/// Round 20 — distribution that can be counted: an arrival through an entry link (<c>/go/{source}</c>, GoEndpoints) is
/// a row per allowlisted source per day, and the source rides the redirect's query into the guest check and the signup
/// (OutfitCheck.Source, AppUser.Source), so <see cref="ComputeAsync"/> can attribute those to the link they followed.
/// A launch from the home screen is one more row, told by the installed app's first call of the day.
/// </para>
/// </summary>
public static partial class Funnel
{
    /// <summary>Days on the numbers page's table, today last.</summary>
    public const int Days = 14;

    /// <summary>Landing page views (the middleware).</summary>
    public static string Landing(DateOnly day) => $"funnel:landing:{Key(day)}";

    /// <summary>Arrivals on a look's public page.</summary>
    public static string LookArrivals(DateOnly day) => $"arrivals:look:{Key(day)}";

    /// <summary>Arrivals on a look's public page that carried <c>?via=share</c>: the share loop, closing.</summary>
    public static string ShareArrivals(DateOnly day) => $"arrivals:look:share:{Key(day)}";

    /// <summary>Arrivals on a profile's public page.</summary>
    public static string ProfileArrivals(DateOnly day) => $"arrivals:profile:{Key(day)}";

    /// <summary>Arrivals carrying someone's invite (<c>?via=&lt;handle&gt;</c>): invites sent, as far as a server can see one.</summary>
    public static string InviteArrivals(DateOnly day) => $"invites:via:{Key(day)}";

    /// <summary>Round 20: the Pro page opened from the compare screen's refusal (POST /api/funnel/pro-opened, from=compare).</summary>
    public static string ProFromCompare(DateOnly day) => $"funnel:pro:compare:{Key(day)}";

    /// <summary>Round 20: the Pro page opened from the wardrobe's Pro line (POST /api/funnel/pro-opened, from=wardrobe).</summary>
    public static string ProFromWardrobe(DateOnly day) => $"funnel:pro:wardrobe:{Key(day)}";

    /// <summary>The counter for one value of <c>from</c> on <c>POST /api/funnel/pro-opened</c>, or null for anything that is not a surface this counts.</summary>
    public static string? ProOpenedCounter(string? from, DateOnly day) => from switch
    {
        "compare" => ProFromCompare(day),
        "wardrobe" => ProFromWardrobe(day),
        _ => null
    };

    /// <summary>
    /// Round 20 — distribution: one arrival through the entry link <c>/go/{source}</c> (GoEndpoints). The source is an
    /// allowlisted word of <c>[a-z0-9]{1,16}</c> (FunnelOptions.List), so the row name is bounded by construction.
    /// </summary>
    public static string SourceArrivals(string source, DateOnly day) => $"funnel:src:{source}:{Key(day)}";

    /// <summary>
    /// Round 20 review: the guest checks <see cref="GuestCheckSweeper"/> removed unclaimed, a day after they were made, by
    /// the day they were made (not <c>error</c>). The table counts guest checks off the rows and looks back fourteen days,
    /// so without these a guest check left the numbers the day it left the database; the sweeper writes them in the same
    /// transaction as the delete, and <see cref="ComputeAsync"/> adds them to the rows still there.
    /// </summary>
    public static string SweptGuestChecks(DateOnly day) => $"funnel:guest:swept:{Key(day)}";

    /// <summary>Round 20 review: the same, for the swept guest checks that came through the entry link <paramref name="source"/>.</summary>
    public static string SweptGuestChecks(string source, DateOnly day) => $"funnel:guest:swept:{source}:{Key(day)}";

    /// <summary>Round 20: launches from the home screen, one per device-day, told by <see cref="StandaloneHeader"/> on the first call (GET /api/config).</summary>
    public static string Standalone(DateOnly day) => $"funnel:standalone:{Key(day)}";

    /// <summary>The header the installed app puts on its first request of a day; counted on <c>/api/config</c> only, ignored anywhere else.</summary>
    public const string StandaloneHeader = "X-Orevosh-Launch";

    /// <summary>The one value of <see cref="StandaloneHeader"/> that counts.</summary>
    public const string StandaloneValue = "standalone";

    public static string Key(DateOnly day) => day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The fetchers that unfurl a pasted link — WhatsApp, Telegram, Facebook, Slack, Discord, X and the general crawlers.
    /// An entry link they fetch is a paste, not a person, so GoEndpoints redirects them and counts nothing. Nothing
    /// about the user agent is ever stored; it is read once, matched, and forgotten.
    /// </summary>
    [GeneratedRegex(@"bot|crawl|spider|facebookexternalhit|whatsapp|telegrambot|twitterbot|slackbot|discordbot", RegexOptions.IgnoreCase)]
    public static partial Regex CrawlerRegex();

    // The same shape AuthEndpoints accepts as a handle; anything else in ?via is somebody's noise, not an invite.
    [GeneratedRegex(@"^[\p{L}\p{N}_.]{2,40}$")]
    private static partial Regex HandleRegex();

    /// <summary>
    /// The middleware. A GET that is a page (never <c>/api</c>, never a file with an extension) counts: a landing view
    /// when it is under <c>/landing</c>, an invite arrival when the query carries a <c>via</c> that is a handle rather
    /// than the share marker. Both are one upsert on a <see cref="Counter"/> row and neither is ever worth failing a
    /// request for.
    /// </summary>
    public static async Task Count(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        if (HttpMethods.IsGet(request.Method))
        {
            if (request.Path.StartsWithSegments("/api"))
            {
                // Round 20: the installed app's first call of the day says so, on this one route and no other, so one
                // launch is one row however many calls follow it. Any other path carrying the header is ignored.
                if (request.Path == "/api/config" && string.Equals(request.Headers[StandaloneHeader].ToString(), StandaloneValue, StringComparison.OrdinalIgnoreCase))
                {
                    await TallyAsync(context, Standalone(DateOnly.FromDateTime(DateTime.UtcNow)));
                }
            }
            else
            {
                var path = request.Path.Value ?? "/";
                var landing = IsLandingPage(path);
                var via = request.Query["via"].ToString().Trim();
                var invite = via.Length > 0
                    && !string.Equals(via, PublicPageEndpoints.ViaShare, StringComparison.OrdinalIgnoreCase)
                    && HandleRegex().IsMatch(via);
                if (landing || invite)
                {
                    var day = DateOnly.FromDateTime(DateTime.UtcNow);
                    if (landing)
                    {
                        await TallyAsync(context, Landing(day));
                    }

                    if (invite)
                    {
                        await TallyAsync(context, InviteArrivals(day));
                    }
                }
            }
        }

        await next(context);
    }

    /// <summary>One upsert on a counter row, inside the request's own scope; a tally is never worth a page.</summary>
    private static async Task TallyAsync(HttpContext context, string name)
    {
        try
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            await Counters.IncrementAsync(db, name, context.RequestAborted);
        }
        catch (Exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            // A tally is never worth a page.
        }
    }

    /// <summary>The landing page itself, not the screenshots beside it: "/landing", "/landing/" or a ".html" under it.</summary>
    public static bool IsLandingPage(string path)
    {
        if (!path.StartsWith("/landing", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = path["/landing".Length..];
        return rest.Length == 0 || rest == "/" || rest.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The last <see cref="Days"/> days of the funnel, oldest first, plus today's conversion between the steps and the
    /// invite numbers. Read straight off the counters and the rows; nothing is cached, and a moderator is the only one
    /// who ever asks (MetricsEndpoints is the gate).
    /// </summary>
    public static async Task<FunnelMetricsDto> ComputeAsync(AppDbContext db, DateTime nowUtc, IReadOnlyList<string> sources, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(nowUtc);
        var first = today.AddDays(-(Days - 1));
        var from = first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // One read of every counter in the window, then the rows that already say what happened.
        var names = new List<string>();
        for (var day = first; day <= today; day = day.AddDays(1))
        {
            names.Add(Landing(day));
            names.Add(LookArrivals(day));
            names.Add(ShareArrivals(day));
            names.Add(ProfileArrivals(day));
            names.Add(InviteArrivals(day));
            names.Add(ProFromCompare(day));
            names.Add(ProFromWardrobe(day));
            names.Add(Standalone(day));
            names.Add(SweptGuestChecks(day));
            // Round 20: the entry links, one row per allowlisted source per day (a handful times fourteen), and the guest
            // checks through each that the sweeper has since removed.
            foreach (var source in sources)
            {
                names.Add(SourceArrivals(source, day));
                names.Add(SweptGuestChecks(source, day));
            }
        }

        var tallies = await db.Counters.AsNoTracking().Where(c => names.Contains(c.Name)).ToDictionaryAsync(c => c.Name, c => c.Value, ct);
        long Tally(string name) => tallies.TryGetValue(name, out var value) ? value : 0;

        // A guest check is one that was made without an account: still a guest's, or claimed by the account it followed.
        // Round 20: each row carries the entry link it came through (Source), so the same reads feed the per-source table.
        // An unclaimed one is swept a day after it was made; what the sweeper removed is in the SweptGuestChecks tallies.
        var guestRows = await db.Checks.AsNoTracking()
            .Where(c => c.CreatedAt >= from && c.Status != CheckStatus.Error && (c.GuestToken != null || c.ClaimedAt != null))
            .Select(c => new { c.CreatedAt, c.Source })
            .ToListAsync(ct);
        var signupRows = await db.Users.AsNoTracking().Where(u => u.CreatedAt >= from).Select(u => new { u.CreatedAt, u.Source }).ToListAsync(ct);
        // A first post is the day an account posted for the first time ever, so a busy poster counts once.
        var firstPostRows = (await db.Posts.AsNoTracking()
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, First = g.Min(p => p.CreatedAt) })
            .ToListAsync(ct))
            .Where(r => r.First >= from)
            .ToList();
        var firstPosterIds = firstPostRows.Select(r => r.UserId).ToList();
        var posterSources = await db.Users.AsNoTracking()
            .Where(u => firstPosterIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Source })
            .ToDictionaryAsync(u => u.Id, u => u.Source, ct);
        var guestChecks = guestRows.Select(r => r.CreatedAt).ToList();
        var signups = signupRows.Select(r => r.CreatedAt).ToList();
        var firstPosts = firstPostRows.Select(r => r.First).ToList();

        static int OnDay(IEnumerable<DateTime> times, DateOnly day) => times.Count(t => DateOnly.FromDateTime(t) == day);

        var rows = new List<FunnelDayDto>();
        for (var day = first; day <= today; day = day.AddDays(1))
        {
            rows.Add(new FunnelDayDto(
                Day: day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Landing: Clamp(Tally(Landing(day))),
                GuestChecks: Clamp(OnDay(guestChecks, day) + Tally(SweptGuestChecks(day))),
                Signups: OnDay(signups, day),
                FirstPosts: OnDay(firstPosts, day),
                LookArrivals: Clamp(Tally(LookArrivals(day))),
                ShareArrivals: Clamp(Tally(ShareArrivals(day))),
                ProfileArrivals: Clamp(Tally(ProfileArrivals(day))),
                Invites: Clamp(Tally(InviteArrivals(day))),
                ProFromCompare: Clamp(Tally(ProFromCompare(day))),
                ProFromWardrobe: Clamp(Tally(ProFromWardrobe(day))),
                Standalone: Clamp(Tally(Standalone(day)))));
        }

        // Round 20: the window's totals per entry link, in the allowlist's order, zero rows kept so the table is stable and
        // a link nobody followed reads as a visible zero rather than a missing row. Not per day: a per-source-per-day
        // table is a hundred cells of mostly zeros on a pilot.
        var perSource = new List<FunnelSourceDto>();
        foreach (var source in sources)
        {
            long arrivals = 0;
            long swept = 0;
            for (var day = first; day <= today; day = day.AddDays(1))
            {
                arrivals += Tally(SourceArrivals(source, day));
                swept += Tally(SweptGuestChecks(source, day));
            }

            perSource.Add(new FunnelSourceDto(
                Source: source,
                Arrivals: Clamp(arrivals),
                GuestChecks: Clamp(guestRows.Count(r => r.Source == source) + swept),
                Signups: signupRows.Count(r => r.Source == source),
                FirstPosts: firstPostRows.Count(r => posterSources.TryGetValue(r.UserId, out var src) && src == source)));
        }

        var last = rows[^1];
        var conversion = new FunnelConversionDto(
            LandingToGuestCheck: Rate(last.GuestChecks, last.Landing),
            GuestCheckToSignup: Rate(last.Signups, last.GuestChecks),
            SignupToFirstPost: Rate(last.FirstPosts, last.Signups),
            FirstPostToArrival: Rate(last.LookArrivals, last.FirstPosts),
            ArrivalFromShare: Rate(last.ShareArrivals, last.LookArrivals));

        // Invites: sent is what the server can honestly see (an arrival carrying someone's ?via), accepted is the
        // accounts that named an inviter at signup, and the top inviters are handles, so the page is moderators' only.
        var accepted = await db.Users.AsNoTracking().CountAsync(u => u.InvitedByUserId != null, ct);
        var top = await db.Users.AsNoTracking()
            .Where(u => u.InvitedByUserId != null)
            .GroupBy(u => u.InvitedByUserId!.Value)
            .Select(g => new { UserId = g.Key, Accepted = g.Count() })
            .OrderByDescending(g => g.Accepted)
            .Take(10)
            .ToListAsync(ct);
        var handles = await db.Users.AsNoTracking()
            .Where(u => top.Select(t => t.UserId).Contains(u.Id))
            .Select(u => new { u.Id, u.Handle })
            .ToDictionaryAsync(u => u.Id, u => u.Handle, ct);
        var inviters = top
            .Where(t => handles.ContainsKey(t.UserId))
            .Select(t => new InviterDto(handles[t.UserId], t.Accepted))
            .ToList();

        var sent = 0;
        foreach (var row in rows)
        {
            sent += row.Invites;
        }

        return new FunnelMetricsDto(rows, conversion, new InviteMetricsDto(sent, accepted, inviters), perSource);
    }

    /// <summary>A step over the one before it, four decimals; null when the step before it never happened.</summary>
    public static double? Rate(int step, int before) => before <= 0 ? null : Math.Round((double)step / before, 4);

    private static int Clamp(long value) => (int)Math.Min(int.MaxValue, Math.Max(0, value));
}
