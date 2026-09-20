using System.Globalization;
using System.Net;
using System.Text;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// Round 13 — the growth loop: a look has a public address. Server-rendered HTML that needs no session and no client
/// JavaScript, so a link pasted into WhatsApp, Telegram, Facebook or X unfurls with the look on it and a tap lands on
/// something readable even before the app has loaded.
/// <list type="bullet">
/// <item><c>GET /look/{id}</c> — one posted look: the photo, the score ring (inline SVG), the handle, the stylist's
/// headline and the one tip, "Check yours" (the app) and "Open in OREVOSH" (<c>/#/post/{id}</c>). Open Graph and
/// Twitter tags carry the title (the headline, or "@handle's look on OREVOSH"), the description (the score, the intent
/// and the tip — the tip is what makes people tap) and <c>og:image</c>, which is <see cref="ImagePath"/> below.</item>
/// <item><c>GET /look/{id}/image</c> — the look's photo, public for a posted, visible look whose author is not
/// suspended, 404 for anything else. The photo of a check that was never posted, of a hidden look or of a suspended
/// account never leaves through this door; <c>/api/posts/{id}/image</c> stays the app's own route with its private
/// cache rule, and this one is the public one a crawler may fetch and a CDN may keep.</item>
/// <item><c>GET /u/{handle}</c> — the person's visible looks as a grid, with the same tags.</item>
/// <item><c>GET /digest/off/{token}</c> — the signed unsubscribe link from the weekly mail (Services/Digest.cs):
/// flips <see cref="AppUser.DigestOn"/> off with no login and says so on a small page.</item>
/// </list>
/// <para>
/// Every arrival is tallied per day in <see cref="Counter"/> rows (<c>arrivals:look:yyyyMMdd</c>,
/// <c>arrivals:look:share:yyyyMMdd</c> for a <c>?via=share</c>, <c>arrivals:profile:yyyyMMdd</c>), so the numbers page
/// can see the loop turn. No cookie is set and nothing third-party is asked for: the page is one HTML document, the
/// photo, the wordmark and the brand's own faces as the system has them. The direction and the language are the look's,
/// not the reader's: the stylist wrote those words in that language and they are never re-displayed in another.
/// </para>
/// </summary>
public static class PublicPageEndpoints
{
    /// <summary>The look page's path, without the id: <c>{origin}/look/{id}</c> is the address a share carries.</summary>
    public const string LookPath = "/look";

    /// <summary>The profile page's path, without the handle.</summary>
    public const string ProfilePath = "/u";

    /// <summary>The unsubscribe page's path, without the token.</summary>
    public const string DigestOffPath = "/digest/off";

    /// <summary>The value of <c>?via</c> that marks an arrival from a share card or a share video, not from an invite.</summary>
    public const string ViaShare = "share";

    /// <summary>Looks on a profile page. A grid, not a feed: the app is where the scrolling happens.</summary>
    public const int ProfileLooks = 12;

    public static IEndpointRouteBuilder MapPublicPageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(LookPath + "/{id:guid}", LookPageAsync);
        app.MapGet(LookPath + "/{id:guid}/image", LookImageAsync);
        app.MapGet(ProfilePath + "/{handle}", ProfilePageAsync);
        app.MapGet(DigestOffPath + "/{token}", DigestOffAsync);
        // The same feature's switch inside the app: Settings reads it and flips it. The mailed link needs no session
        // (the person is in their inbox); this one is the account's own and needs one like every other preference.
        app.MapGet(DigestStatePath, GetDigestStateAsync).RequireAuthorization();
        app.MapPost(DigestStatePath, SetDigestStateAsync).RequireAuthorization();
        return app;
    }

    /// <summary>The weekly mail's switch in Settings: GET reads it, POST sets it.</summary>
    public const string DigestStatePath = "/api/users/me/digest";

    /// <summary>The public address of a look, as a share carries it and as the digest mail links it.</summary>
    public static string LookUrl(string origin, Guid postId) => $"{origin}{LookPath}/{postId}";

    /// <summary>The public address of a profile.</summary>
    public static string ProfileUrl(string origin, string handle) => $"{origin}{ProfilePath}/{Uri.EscapeDataString(handle)}";

    /// <summary>The public image route of a look: what <c>og:image</c> points at.</summary>
    public static string ImagePath(Guid postId) => $"{LookPath}/{postId}/image";

    /// <summary>
    /// The origin these pages call their own: Email:PublicOrigin, else Billing:PublicOrigin, else the request's own
    /// scheme and host. Unlike a mailed link (RecoveryTokens.TryOrigin), a page's canonical and og:url may be built from
    /// the request: the reader already reached the page at that address, and nothing secret rides in the URL.
    /// </summary>
    public static string Origin(HttpRequest request, IConfiguration configuration)
    {
        foreach (var key in new[] { "Email:PublicOrigin", "Billing:PublicOrigin" })
        {
            var configured = (configuration[key] ?? "").Trim().TrimEnd('/');
            if (configured.Length > 0)
            {
                return configured;
            }
        }

        return $"{request.Scheme}://{request.Host}";
    }

    // ---------- the look ----------

    private static async Task<IResult> LookPageAsync(Guid id, HttpContext context, AppDbContext db, Localizer localizer, IConfiguration configuration, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await CountAsync(db, Funnel.LookArrivals(today), ct);
        if (string.Equals(context.Request.Query["via"].ToString(), ViaShare, StringComparison.OrdinalIgnoreCase))
        {
            await CountAsync(db, Funnel.ShareArrivals(today), ct);
        }

        var look = await FindLookAsync(db, id, ct);
        if (look is null)
        {
            return NotFoundPage(context, localizer);
        }

        var language = Localizer.IsSupported(look.Language) ? look.Language : Localizer.DefaultLocale;
        var origin = Origin(context.Request, configuration);
        var url = LookUrl(origin, look.PostId);
        var image = origin + ImagePath(look.PostId);
        var intent = localizer.Get(language, "public.intent." + look.Intent);
        var title = look.Headline.Length > 0 ? look.Headline : localizer.Get(language, "public.look_title", look.Handle);
        var description = look.Tip.Length > 0
            ? localizer.Get(language, "public.look_description", look.Score, intent, look.Tip)
            : localizer.Get(language, "public.look_description_short", look.Score, intent);
        var alt = localizer.Get(language, "public.photo_alt", look.Handle);

        var body = new StringBuilder();
        body.Append("<main class=\"look\">");
        body.Append(Wordmark(origin));
        body.Append("<figure class=\"photo\"><img src=\"").Append(Esc(image)).Append("\" alt=\"").Append(Esc(alt)).Append("\"></figure>");
        body.Append("<div class=\"meta\">");
        body.Append(Ring(look.Score, localizer.Get(language, "public.score_out_of", look.Score)));
        body.Append("<div class=\"who\"><p class=\"handle\" dir=\"ltr\">@").Append(Esc(look.Handle)).Append("</p>");
        body.Append("<p class=\"intent\">").Append(Esc(intent)).Append("</p></div>");
        body.Append("</div>");
        body.Append("<h1>").Append(Esc(title)).Append("</h1>");
        if (look.Tip.Length > 0)
        {
            body.Append("<p class=\"tip\"><span class=\"lbl\">").Append(Esc(localizer.Get(language, "public.tip"))).Append("</span>")
                .Append(Esc(look.Tip)).Append("</p>");
        }

        body.Append("<div class=\"actions\">");
        body.Append("<a class=\"btn\" href=\"").Append(Esc(origin)).Append("/\">").Append(Esc(localizer.Get(language, "public.check_yours"))).Append("</a>");
        body.Append("<a class=\"btn ghost\" href=\"").Append(Esc(origin)).Append("/#/post/").Append(look.PostId).Append("\">")
            .Append(Esc(localizer.Get(language, "public.open_app"))).Append("</a>");
        body.Append("</div>");
        body.Append("<p class=\"tagline\">").Append(Esc(localizer.Get(language, "public.tagline"))).Append("</p>");
        body.Append("</main>");

        return Page(context, new PageHead(
            Language: language,
            Title: title,
            Description: description,
            CanonicalUrl: url,
            ImageUrl: image,
            ImageAlt: alt,
            Index: true,
            OgType: "article"), body.ToString());
    }

    /// <summary>
    /// The public photo of a posted look. The rules are the strict ones: a post that exists and is not hidden, whose
    /// author is not suspended, and whose check still has its file. Anything else is a 404 — never a photo that is not
    /// public. Cached for an hour by anything that wants to (a crawler fetches it once, a CDN may keep it).
    /// </summary>
    private static async Task<IResult> LookImageAsync(Guid id, HttpContext context, AppDbContext db, IImageStore images, CancellationToken ct)
    {
        var look = await FindLookAsync(db, id, ct);
        var stream = look is null ? null : images.OpenRead(look.ImagePath);
        if (stream is null)
        {
            return Results.NotFound();
        }

        var mediaType = Path.GetExtension(look!.ImagePath) switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        context.Response.Headers.CacheControl = "public, max-age=3600";
        return Results.Stream(stream, mediaType);
    }

    // ---------- the profile ----------

    private static async Task<IResult> ProfilePageAsync(string handle, HttpContext context, AppDbContext db, Localizer localizer, IConfiguration configuration, CancellationToken ct)
    {
        await CountAsync(db, Funnel.ProfileArrivals(DateOnly.FromDateTime(DateTime.UtcNow)), ct);

        var lower = (handle ?? "").Trim().ToLowerInvariant();
        var user = lower.Length == 0 ? null : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.HandleLower == lower && !u.Suspended, ct);
        if (user is null)
        {
            return NotFoundPage(context, localizer);
        }

        var language = Localizer.IsSupported(user.PreferredLanguage) ? user.PreferredLanguage : Localizer.DefaultLocale;
        var origin = Origin(context.Request, configuration);
        var url = ProfileUrl(origin, user.Handle);
        var looks = await db.Posts.AsNoTracking()
            .Where(p => p.UserId == user.Id && !p.Hidden)
            .OrderByDescending(p => p.CreatedAt)
            .Take(ProfileLooks)
            .Select(p => new { p.Id, p.Score })
            .ToListAsync(ct);

        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Handle : user.DisplayName!;
        var title = localizer.Get(language, "public.profile_title", user.Handle);
        var description = localizer.Get(language, "public.profile_description", user.Handle);
        var alt = localizer.Get(language, "public.photo_alt", user.Handle);
        var image = looks.Count > 0 ? origin + ImagePath(looks[0].Id) : origin + "/brand/og-1200x630.png";

        var body = new StringBuilder();
        body.Append("<main class=\"profile\">");
        body.Append(Wordmark(origin));
        body.Append("<h1>").Append(Esc(name)).Append("</h1>");
        body.Append("<p class=\"handle\" dir=\"ltr\">@").Append(Esc(user.Handle)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(user.Bio))
        {
            body.Append("<p class=\"bio\">").Append(Esc(user.Bio!)).Append("</p>");
        }

        if (looks.Count == 0)
        {
            body.Append("<p class=\"empty\">").Append(Esc(localizer.Get(language, "public.profile_empty"))).Append("</p>");
        }
        else
        {
            body.Append("<h2 class=\"lbl\">").Append(Esc(localizer.Get(language, "public.profile_looks"))).Append("</h2>");
            body.Append("<ul class=\"grid\">");
            foreach (var look in looks)
            {
                body.Append("<li><a href=\"").Append(Esc(LookUrl(origin, look.Id))).Append("\">");
                body.Append("<img src=\"").Append(Esc(origin + ImagePath(look.Id))).Append("\" alt=\"").Append(Esc(alt)).Append("\" loading=\"lazy\">");
                body.Append("<span class=\"n\">").Append(look.Score.ToString(CultureInfo.InvariantCulture)).Append("</span>");
                body.Append("</a></li>");
            }

            body.Append("</ul>");
        }

        body.Append("<div class=\"actions\"><a class=\"btn\" href=\"").Append(Esc(origin)).Append("/\">")
            .Append(Esc(localizer.Get(language, "public.check_yours"))).Append("</a>");
        body.Append("<a class=\"btn ghost\" href=\"").Append(Esc(origin)).Append("/#/u/").Append(Esc(Uri.EscapeDataString(user.Handle))).Append("\">")
            .Append(Esc(localizer.Get(language, "public.open_app"))).Append("</a></div>");
        body.Append("<p class=\"tagline\">").Append(Esc(localizer.Get(language, "public.tagline"))).Append("</p>");
        body.Append("</main>");

        return Page(context, new PageHead(language, title, description, url, image, alt, Index: true, OgType: "profile"), body.ToString());
    }

    // ---------- the weekly mail's unsubscribe link ----------

    /// <summary>
    /// The one-tap unsubscribe from the weekly mail: the token is an HMAC of the account id (Services/Digest.cs), so
    /// nobody can turn off someone else's mail, and no session is needed — the person is in their inbox, not in the app.
    /// A stale or forged token gets the same small page with a line saying so, never an account's name.
    /// </summary>
    private static async Task<IResult> DigestOffAsync(
        string token, HttpContext context, AppDbContext db, Localizer localizer, DigestTokens tokens, IConfiguration configuration, CancellationToken ct)
    {
        var user = await tokens.FindAsync(db, token, ct);
        var language = user is not null && Localizer.IsSupported(user.PreferredLanguage) ? user.PreferredLanguage : Localizer.Resolve(null, context.Request);
        var origin = Origin(context.Request, configuration);
        if (user is { DigestOn: true })
        {
            user.DigestOn = false;
            await db.SaveChangesAsync(ct);
        }

        var title = localizer.Get(language, user is null ? "digest.off_invalid_title" : "digest.off_title");
        var text = localizer.Get(language, user is null ? "digest.off_invalid_body" : "digest.off_body");
        var body = new StringBuilder();
        body.Append("<main class=\"note\">");
        body.Append(Wordmark(origin));
        body.Append("<h1>").Append(Esc(title)).Append("</h1>");
        body.Append("<p>").Append(Esc(text)).Append("</p>");
        body.Append("<div class=\"actions\"><a class=\"btn\" href=\"").Append(Esc(origin)).Append("/#/settings\">")
            .Append(Esc(localizer.Get(language, "digest.off_settings"))).Append("</a></div>");
        body.Append("</main>");

        // Never cached and never indexed: it is one person's link and it changes something.
        context.Response.Headers.CacheControl = "no-store";
        return Page(context, new PageHead(language, title, text, CanonicalUrl: null, ImageUrl: null, ImageAlt: null, Index: false, OgType: "website"), body.ToString());
    }

    // ---------- the weekly mail's switch ----------

    private static async Task<IResult> GetDigestStateAsync(HttpContext context, AppDbContext db, Localizer localizer, IEmailSender email, CancellationToken ct)
    {
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        return user is null ? failure! : Results.Json(StateOf(user, email), AppJson.Options);
    }

    private static async Task<IResult> SetDigestStateAsync(DigestRequest? body, HttpContext context, AppDbContext db, Localizer localizer, IEmailSender email, CancellationToken ct)
    {
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        if (body?.On is not { } on)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.invalid_request"));
        }

        user.DigestOn = on;
        await db.SaveChangesAsync(ct);
        return Results.Json(StateOf(user, email), AppJson.Options);
    }

    /// <summary>
    /// The switch and whether anything could come of it: mail has to be on for this server and the address has to be one
    /// the person confirmed, or the toggle is a promise nothing keeps. Settings says which of the two is missing.
    /// </summary>
    private static DigestStateDto StateOf(AppUser user, IEmailSender email) =>
        new(user.DigestOn, email.Enabled && user.Email is not null && user.EmailVerifiedAt is not null);

    // ---------- reading a look ----------

    /// <summary>What the public page shows of a look, read in one query.</summary>
    private sealed record PublicLook(Guid PostId, string Handle, string Headline, int Score, StyleIntent Intent, string Language, string Tip, string ImagePath);

    /// <summary>
    /// The look behind a public address, or null: the post must exist and not be hidden, its author must not be
    /// suspended, and the check must still have its photo. The tip comes out of the stored feedback, which is the only
    /// place it lives.
    /// </summary>
    private static async Task<PublicLook?> FindLookAsync(AppDbContext db, Guid postId, CancellationToken ct)
    {
        var row = await db.Posts.AsNoTracking()
            .Where(p => p.Id == postId && !p.Hidden)
            .Join(db.Users.AsNoTracking().Where(u => !u.Suspended), p => p.UserId, u => u.Id, (p, u) => new { Post = p, User = u })
            .Join(db.Checks.AsNoTracking(), pu => pu.Post.CheckId, c => c.Id, (pu, c) => new
            {
                pu.Post.Id,
                pu.User.Handle,
                pu.Post.Headline,
                pu.Post.Score,
                pu.Post.Intent,
                c.Language,
                c.FeedbackJson,
                c.ImagePath
            })
            .FirstOrDefaultAsync(ct);
        if (row is null || string.IsNullOrEmpty(row.ImagePath))
        {
            return null;
        }

        var tip = "";
        try
        {
            tip = row.FeedbackJson is null ? "" : System.Text.Json.JsonSerializer.Deserialize<OutfitFeedback>(row.FeedbackJson, AppJson.Options)?.OneTip ?? "";
        }
        catch (System.Text.Json.JsonException)
        {
            // A feedback document this server can no longer read is a page without a tip, not a 500.
        }

        return new PublicLook(row.Id, row.Handle, row.Headline ?? "", row.Score, row.Intent, row.Language ?? Localizer.DefaultLocale, tip, row.ImagePath);
    }

    // ---------- the document ----------

    /// <summary>Everything the &lt;head&gt; needs. A page with no canonical URL (the unsubscribe note) carries no link tags either.</summary>
    private sealed record PageHead(string Language, string Title, string Description, string? CanonicalUrl, string? ImageUrl, string? ImageAlt, bool Index, string OgType);

    private static readonly string[] RtlLanguages = ["he", "ar"];

    /// <summary>
    /// The whole document: the tags a crawler reads, then the body. No script anywhere (the app's CSP allows none off
    /// this origin and this page needs none), and no web font fetched from a third party — the brand's faces are asked
    /// for by name and the system's own stack stands behind them, so the page paints on the first byte even on a cold
    /// tap and a crawler's fetch costs one request.
    /// </summary>
    private static IResult Page(HttpContext context, PageHead head, string body)
    {
        var dir = RtlLanguages.Contains(head.Language) ? "rtl" : "ltr";
        var documentTitle = head.Title + " — OREVOSH";
        var html = new StringBuilder();
        html.Append("<!doctype html>\n<html lang=\"").Append(head.Language).Append("\" dir=\"").Append(dir).Append("\">\n<head>\n");
        html.Append("<meta charset=\"utf-8\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1, viewport-fit=cover\">\n");
        html.Append("<meta name=\"color-scheme\" content=\"dark\">\n");
        html.Append("<meta name=\"theme-color\" content=\"#0b0b0f\">\n");
        html.Append("<title>").Append(Esc(documentTitle)).Append("</title>\n");
        html.Append("<meta name=\"description\" content=\"").Append(Esc(head.Description)).Append("\">\n");
        html.Append("<meta name=\"robots\" content=\"").Append(head.Index ? "index, follow, max-image-preview:large" : "noindex, nofollow").Append("\">\n");
        if (head.CanonicalUrl is { Length: > 0 } canonical)
        {
            html.Append("<link rel=\"canonical\" href=\"").Append(Esc(canonical)).Append("\">\n");
            html.Append("<meta property=\"og:url\" content=\"").Append(Esc(canonical)).Append("\">\n");
        }

        html.Append("<meta property=\"og:type\" content=\"").Append(head.OgType).Append("\">\n");
        html.Append("<meta property=\"og:site_name\" content=\"OREVOSH\">\n");
        html.Append("<meta property=\"og:title\" content=\"").Append(Esc(head.Title)).Append("\">\n");
        html.Append("<meta property=\"og:description\" content=\"").Append(Esc(head.Description)).Append("\">\n");
        html.Append("<meta property=\"og:locale\" content=\"").Append(head.Language).Append("\">\n");
        if (head.ImageUrl is { Length: > 0 } image)
        {
            html.Append("<meta property=\"og:image\" content=\"").Append(Esc(image)).Append("\">\n");
            html.Append("<meta property=\"og:image:alt\" content=\"").Append(Esc(head.ImageAlt ?? head.Title)).Append("\">\n");
            html.Append("<meta name=\"twitter:image\" content=\"").Append(Esc(image)).Append("\">\n");
        }

        html.Append("<meta name=\"twitter:card\" content=\"summary_large_image\">\n");
        html.Append("<meta name=\"twitter:title\" content=\"").Append(Esc(head.Title)).Append("\">\n");
        html.Append("<meta name=\"twitter:description\" content=\"").Append(Esc(head.Description)).Append("\">\n");
        html.Append("<link rel=\"icon\" href=\"/favicon.svg\" type=\"image/svg+xml\">\n");
        html.Append("<style>").Append(Css).Append("</style>\n");
        html.Append("</head>\n<body>\n").Append(body).Append("\n</body>\n</html>\n");

        if (!context.Response.Headers.ContainsKey("Cache-Control"))
        {
            // Revalidated on every load, so an arrival is an arrival and an edited look never sits in a cache.
            context.Response.Headers.CacheControl = "no-cache";
        }

        return Results.Text(html.ToString(), "text/html; charset=utf-8");
    }

    private static IResult NotFoundPage(HttpContext context, Localizer localizer)
    {
        var language = Localizer.Resolve(null, context.Request);
        var title = localizer.Get(language, "public.not_found_title");
        var text = localizer.Get(language, "public.not_found_body");
        var body = "<main class=\"note\">" + Wordmark("")
            + "<h1>" + Esc(title) + "</h1><p>" + Esc(text) + "</p>"
            + "<div class=\"actions\"><a class=\"btn\" href=\"/\">" + Esc(localizer.Get(language, "public.check_yours")) + "</a></div></main>";
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Page(context, new PageHead(language, title, text, null, null, null, Index: false, OgType: "website"), body);
    }

    /// <summary>The wordmark, as an image from this origin: a logo, never mirrored, and the way back to the app.</summary>
    private static string Wordmark(string origin) =>
        "<a class=\"wordmark\" href=\"" + Esc(origin.Length > 0 ? origin + "/" : "/") + "\" aria-label=\"OREVOSH\">"
        + "<img src=\"/brand/wordmark.svg\" alt=\"OREVOSH\" width=\"1626\" height=\"350\"></a>";

    /// <summary>
    /// The score ring as inline SVG: the same idea as the app's badge — a track, the lilac-to-rose arc for score ⁄ 10
    /// drawn from the top, and the number in the middle. Inline, so the page needs no second request for it.
    /// </summary>
    private static string Ring(int score, string label)
    {
        var clamped = Math.Clamp(score, 0, 10);
        const double radius = 26;
        var circumference = 2 * Math.PI * radius;
        var arc = (circumference * clamped / 10).ToString("0.##", CultureInfo.InvariantCulture);
        var rest = (circumference - (circumference * clamped / 10)).ToString("0.##", CultureInfo.InvariantCulture);
        return "<div class=\"ring\" role=\"img\" aria-label=\"" + Esc(label) + "\">"
            + "<svg viewBox=\"0 0 64 64\" width=\"64\" height=\"64\" aria-hidden=\"true\" focusable=\"false\">"
            + "<defs><linearGradient id=\"ovp\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">"
            + "<stop offset=\"0\" stop-color=\"#b39dff\"/><stop offset=\"1\" stop-color=\"#ff8fb1\"/></linearGradient></defs>"
            + "<circle cx=\"32\" cy=\"32\" r=\"26\" fill=\"none\" stroke=\"#2b2b36\" stroke-width=\"6\"/>"
            + "<circle cx=\"32\" cy=\"32\" r=\"26\" fill=\"none\" stroke=\"url(#ovp)\" stroke-width=\"6\" stroke-linecap=\"round\""
            + " stroke-dasharray=\"" + arc + " " + rest + "\" transform=\"rotate(-90 32 32)\"/></svg>"
            + "<span class=\"n\">" + clamped.ToString(CultureInfo.InvariantCulture) + "</span></div>";
    }

    /// <summary>Counts an arrival; a tally is never worth failing a page for.</summary>
    private static async Task CountAsync(AppDbContext db, string name, CancellationToken ct)
    {
        try
        {
            await Counters.IncrementAsync(db, name, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // The page is what the person came for.
        }
    }

    private static string Esc(string text) => WebUtility.HtmlEncode(text);

    /// <summary>
    /// The page's own stylesheet, inline: the app's tokens (DESIGN.md §1), logical properties so Hebrew and Arabic
    /// mirror for free, 44px targets, and the brand's faces by name over the system stack — no font is fetched from
    /// anywhere, which is what keeps a cold tap instant and a crawler's fetch cheap.
    /// </summary>
    private const string Css = """
:root { --bg:#0b0b0f; --bg-2:#13121a; --surface:#15151c; --line:#2b2b36; --ink:#f4f4f7; --ink-2:#b9b9c6; --ink-3:#7f7f8e;
  --accent:#b39dff; --accent-2:#ff8fb1; --accent-ink:#150f2e; --grad:linear-gradient(135deg,#b39dff 0%,#ff8fb1 100%);
  --radius:18px; --radius-sm:12px; --pill:999px;
  --font-display:"Outfit","Heebo","Cairo",system-ui,sans-serif;
  --font-body:"Heebo","Cairo",system-ui,-apple-system,"Segoe UI",Roboto,"Noto Sans Hebrew","Noto Sans Arabic",sans-serif; }
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); background-image:radial-gradient(120% 60% at 50% 0%, var(--bg-2) 0%, var(--bg) 60%);
  color:var(--ink); font-family:var(--font-body); font-size:16px; line-height:1.5; -webkit-font-smoothing:antialiased; }
main { max-inline-size:520px; margin:0 auto; padding:20px 16px 48px; }
.wordmark { display:block; inline-size:132px; margin-block-end:18px; }
.wordmark img { inline-size:100%; block-size:auto; display:block; }
.photo { margin:0; border-radius:var(--radius); overflow:hidden; background:var(--surface); }
.photo img { display:block; inline-size:100%; block-size:auto; }
.meta { display:flex; align-items:center; gap:14px; margin-block-start:14px; }
.ring { position:relative; inline-size:64px; block-size:64px; flex:none; }
.ring svg { display:block; }
.ring .n { position:absolute; inset:0; display:grid; place-items:center; font:800 22px/1 var(--font-display); color:var(--ink); direction:ltr; }
.who { min-inline-size:0; }
.handle { margin:0; font:700 16px/1.2 var(--font-display); color:var(--ink); overflow-wrap:anywhere; }
.intent { margin:2px 0 0; font-size:14px; color:var(--ink-3); }
h1 { margin:16px 0 0; font:800 26px/1.2 var(--font-display); letter-spacing:-.02em; text-wrap:balance; unicode-bidi:plaintext; }
h2.lbl { margin:22px 0 10px; font:700 11px/1 var(--font-body); letter-spacing:.12em; text-transform:uppercase; color:var(--ink-3); }
.bio { margin:8px 0 0; color:var(--ink-2); unicode-bidi:plaintext; }
.tip { margin:12px 0 0; padding:14px 16px; background:var(--surface); border-radius:var(--radius-sm); color:var(--ink-2); unicode-bidi:plaintext; }
.tip .lbl { display:block; font:700 11px/1 var(--font-body); letter-spacing:.12em; text-transform:uppercase; color:var(--accent); margin-block-end:6px; }
.actions { display:flex; flex-wrap:wrap; gap:10px; margin-block-start:22px; }
.btn { display:inline-flex; align-items:center; justify-content:center; min-block-size:44px; padding:0 22px; border-radius:var(--pill);
  background:var(--grad); color:var(--accent-ink); font-family:var(--font-display); font-weight:700; text-decoration:none; }
.btn.ghost { background:transparent; color:var(--ink); border:1px solid var(--line); }
.tagline { margin-block-start:26px; color:var(--ink-3); font-size:13px; }
.empty { margin-block-start:18px; color:var(--ink-3); }
.grid { list-style:none; margin:0; padding:0; display:grid; grid-template-columns:repeat(3,1fr); gap:6px; }
.grid a { position:relative; display:block; border-radius:var(--radius-sm); overflow:hidden; background:var(--surface); }
.grid img { display:block; inline-size:100%; block-size:auto; aspect-ratio:4/5; object-fit:cover; }
.grid .n { position:absolute; inset-block-end:6px; inset-inline-start:6px; min-inline-size:22px; padding:2px 6px; border-radius:var(--pill);
  background:rgba(11,11,15,.72); color:var(--ink); font:700 12px/1.4 var(--font-display); text-align:center; direction:ltr; }
.note h1 { margin-block-start:8px; }
.note p { color:var(--ink-2); }
@media (min-width: 560px) { main { padding-block-start:36px; } h1 { font-size:30px; } }
""";
}
