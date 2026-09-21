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
/// carrying an invite's <c>?via=&lt;handle&gt;</c>. It sets no cookie, reads no header beyond the path and the query,
/// stores no address and asks nothing off this machine: a day's number, and nothing that could name a person. It runs
/// before the static files, since a landing page is a static file and would otherwise never reach a handler.
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

    public static string Key(DateOnly day) => day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

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
        if (HttpMethods.IsGet(request.Method) && !request.Path.StartsWithSegments("/api"))
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
                var db = context.RequestServices.GetRequiredService<AppDbContext>();
                try
                {
                    if (landing)
                    {
                        await Counters.IncrementAsync(db, Landing(day), context.RequestAborted);
                    }

                    if (invite)
                    {
                        await Counters.IncrementAsync(db, InviteArrivals(day), context.RequestAborted);
                    }
                }
                catch (Exception) when (!context.RequestAborted.IsCancellationRequested)
                {
                    // A tally is never worth a page.
                }
            }
        }

        await next(context);
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
    public static async Task<FunnelMetricsDto> ComputeAsync(AppDbContext db, DateTime nowUtc, CancellationToken ct)
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
        }

        var tallies = await db.Counters.AsNoTracking().Where(c => names.Contains(c.Name)).ToDictionaryAsync(c => c.Name, c => c.Value, ct);
        long Tally(string name) => tallies.TryGetValue(name, out var value) ? value : 0;

        // A guest check is one that was made without an account: still a guest's, or claimed by the account it followed.
        var guestChecks = await db.Checks.AsNoTracking()
            .Where(c => c.CreatedAt >= from && c.Status != CheckStatus.Error && (c.GuestToken != null || c.ClaimedAt != null))
            .Select(c => c.CreatedAt)
            .ToListAsync(ct);
        var signups = await db.Users.AsNoTracking().Where(u => u.CreatedAt >= from).Select(u => u.CreatedAt).ToListAsync(ct);
        // A first post is the day an account posted for the first time ever, so a busy poster counts once.
        var firstPosts = (await db.Posts.AsNoTracking()
            .GroupBy(p => p.UserId)
            .Select(g => g.Min(p => p.CreatedAt))
            .ToListAsync(ct))
            .Where(at => at >= from)
            .ToList();

        static int OnDay(IEnumerable<DateTime> times, DateOnly day) => times.Count(t => DateOnly.FromDateTime(t) == day);

        var rows = new List<FunnelDayDto>();
        for (var day = first; day <= today; day = day.AddDays(1))
        {
            rows.Add(new FunnelDayDto(
                Day: day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Landing: Clamp(Tally(Landing(day))),
                GuestChecks: OnDay(guestChecks, day),
                Signups: OnDay(signups, day),
                FirstPosts: OnDay(firstPosts, day),
                LookArrivals: Clamp(Tally(LookArrivals(day))),
                ShareArrivals: Clamp(Tally(ShareArrivals(day))),
                ProfileArrivals: Clamp(Tally(ProfileArrivals(day))),
                Invites: Clamp(Tally(InviteArrivals(day)))));
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

        return new FunnelMetricsDto(rows, conversion, new InviteMetricsDto(sent, accepted, inviters));
    }

    /// <summary>A step over the one before it, four decimals; null when the step before it never happened.</summary>
    public static double? Rate(int step, int before) => before <= 0 ? null : Math.Round((double)step / before, 4);

    private static int Clamp(long value) => (int)Math.Min(int.MaxValue, Math.Max(0, value));
}
