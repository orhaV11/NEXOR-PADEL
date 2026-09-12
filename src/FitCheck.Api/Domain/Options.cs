namespace FitCheck.Api.Domain;

public sealed class AnthropicOptions
{
    public const string Section = "Anthropic";

    /// <summary>Must support forced tool use (tool_choice type "tool"): Sonnet 5, Opus 5, the 4.x family, Haiku 4.5.</summary>
    public string Model { get; set; } = "claude-sonnet-5";

    public int MaxTokens { get; set; } = 1200;

    public string BaseUrl { get; set; } = "https://api.anthropic.com";
}

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Directory for private photos. Relative paths resolve against the content root, never wwwroot.</summary>
    public string Root { get; set; } = "storage";

    public long MaxImageBytes { get; set; } = 6 * 1024 * 1024;

    /// <summary>A look clip. The client records at most <see cref="MaxVideoSeconds"/>; the server enforces bytes only.</summary>
    public long MaxVideoBytes { get; set; } = 40 * 1024 * 1024;

    /// <summary>Advisory for the client (recording cap and the picker's duration check). Returned by /api/config.</summary>
    public int MaxVideoSeconds { get; set; } = 30;

    /// <summary>Re-encode clips to H.264 MP4 in the background when ffmpeg is available (WebM from Android cannot play on older iPhones).</summary>
    public bool Transcode { get; set; } = true;

    /// <summary>Path to the ffmpeg binary. Empty means look on PATH.</summary>
    public string FfmpegPath { get; set; } = "";
}

/// <summary>Outgoing mail for verification and password reset links. Host and From empty means mail is off: links are logged instead.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";

    /// <summary>Environment only (Email__Password), never appsettings.</summary>
    public string Password { get; set; } = "";

    public string From { get; set; } = "";
    public bool UseStartTls { get; set; } = true;

    /// <summary>The public https origin used in links, e.g. https://looks.example.com. Empty means the request's own origin.</summary>
    public string PublicOrigin { get; set; } = "";

    public bool Enabled => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>Web Push (VAPID). Both keys empty means push is off and the client never offers it.</summary>
public sealed class PushOptions
{
    public const string Section = "Push";

    /// <summary>Base64url uncompressed P-256 public key, as handed to pushManager.subscribe.</summary>
    public string PublicKey { get; set; } = "";

    /// <summary>Base64url P-256 private key. Environment variable Push__PrivateKey; never in appsettings.</summary>
    public string PrivateKey { get; set; } = "";

    /// <summary>mailto: or https: contact for the push services.</summary>
    public string Subject { get; set; } = "mailto:hello@orevosh.app";

    public bool Enabled => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}

/// <summary>
/// Handles promoted to moderator when the app starts (existing accounts only, see Data/AdminSync.cs) and kept from being
/// registered by anyone else. Case-insensitive. Environment variable Admin__Handles__0 etc. Being a moderator is the
/// persisted <see cref="AppUser.IsAdmin"/> flag; this list is only one way of setting it.
/// </summary>
public sealed class AdminOptions
{
    public const string Section = "Admin";

    public List<string> Handles { get; set; } = [];

    /// <summary>Whether the handle is on the list. Reserved at signup and promoted at start; nothing at request time reads it.</summary>
    public bool Lists(string handle) => Handles.Any(h => string.Equals(h.Trim(), handle, StringComparison.OrdinalIgnoreCase));
}

public sealed class LimitsOptions
{
    public const string Section = "Limits";

    /// <summary>The ceiling per account over a rolling 24 hours, whatever the plan says (Plans:ProChecksPerDay is clamped to it). Cost control.</summary>
    public int ChecksPerDay { get; set; } = 30;

    /// <summary>Ceiling across all users over a rolling 24 hours, so a leaked URL cannot run up an unbounded bill.</summary>
    public int ChecksPerDayGlobal { get; set; } = 1000;

    /// <summary>New accounts per client address per hour. Blunts minting fresh ids to dodge the per-user cap.</summary>
    public int SignupsPerHourPerIp { get; set; } = 50;

    /// <summary>Login attempts per client address per 15 minutes. Slows password guessing.</summary>
    public int LoginsPerQuarterHourPerIp { get; set; } = 30;

    /// <summary>Reports that hide a post pending review.</summary>
    public int ReportsToHide { get; set; } = 3;

    /// <summary>Comments one account may post per hour. A brake on flooding, not a product limit: nobody hits it by hand.</summary>
    public int CommentsPerHour { get; set; } = 30;

    /// <summary>Reports one account may file per hour, looks and comments together. Keeps one person from burying the queue.</summary>
    public int ReportsPerHour { get; set; } = 20;
}

/// <summary>Who may check how often. Every check is a paid model call, so free is a taste and Pro is the habit.</summary>
public sealed class PlanOptions
{
    public const string Section = "Plans";

    /// <summary>Checks (and comparisons) per rolling 24 hours for a free account.</summary>
    public int FreeChecksPerDay { get; set; } = 3;

    /// <summary>For a Pro account. Limits:ChecksPerDayGlobal still caps everyone together.</summary>
    public int ProChecksPerDay { get; set; } = 30;

    /// <summary>
    /// For a guest (no account yet), per guest cookie and per client address: what is counted is a stored check (ok,
    /// not_outfit or rejected, the ones that cost a model call), never a refused upload or a failed call.
    /// </summary>
    public int GuestChecksPerDay { get; set; } = 1;

    /// <summary>
    /// The abuse brake on the anonymous check path: attempts (whatever their outcome) per client address per day, in the
    /// "guest" rate-limit policy, answered 429 error.too_fast beyond it. Well above <see cref="GuestChecksPerDay"/> on
    /// purpose, so a refused photo or a model outage never locks a shared address out of its look.
    /// </summary>
    public int GuestAttemptsPerDay { get; set; } = 20;

    /// <summary>Shown on the Pro screen, e.g. "₪19 / month" or "$5 / month". Empty hides the price.</summary>
    public string ProPriceText { get; set; } = "";

    /// <summary>
    /// Whether "which one?" comparisons and the insights need Pro: POST /api/compare and GET /api/users/me/insights answer
    /// 403 error.pro_required to a free account, and the Pro page lists both as benefits only then. Off by default: Pro is
    /// a cap on a real cost, not a feature wall.
    /// </summary>
    public bool CompareNeedsPro { get; set; } = false;
}

/// <summary>Billing. "manual" means Pro is granted with the --pro command; "stripe" means Checkout and the webhook are live.</summary>
public sealed class BillingOptions
{
    public const string Section = "Billing";

    /// <summary>manual | stripe</summary>
    public string Provider { get; set; } = "manual";

    /// <summary>Environment only (Billing__StripeSecretKey). sk_test_ keys work against Stripe's test mode.</summary>
    public string StripeSecretKey { get; set; } = "";

    /// <summary>The recurring price for Pro (price_...).</summary>
    public string StripePriceId { get; set; } = "";

    /// <summary>Signs the webhook events (whsec_...). Environment only.</summary>
    public string StripeWebhookSecret { get; set; } = "";

    /// <summary>Checkout returns to this origin (+ /#/pro?checkout=success|cancel). Empty means the request's origin.</summary>
    public string PublicOrigin { get; set; } = "";

    public bool StripeEnabled => Provider.Equals("stripe", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(StripeSecretKey) && !string.IsNullOrWhiteSpace(StripePriceId) && !string.IsNullOrWhiteSpace(StripeWebhookSecret);
}

/// <summary>
/// The weekly flames board (Round 10): when a week starts and ends, which fires count, how long the lists are. The
/// anti-gaming numbers are the product: fires from people who use the app (a check) and only a few per pair, so friends
/// cannot carry a look.
/// </summary>
public sealed class BoardOptions
{
    public const string Section = "Board";

    /// <summary>The first day of the board's week, in <see cref="TimeZone"/>.</summary>
    public DayOfWeek WeekStartsOn { get; set; } = DayOfWeek.Sunday;

    /// <summary>IANA zone the week is cut in. Israel's week starts on Sunday; the close is Saturday midnight there.</summary>
    public string TimeZone { get; set; } = "Asia/Jerusalem";

    /// <summary>A fire counts only when the firer has made at least this many checks. 0 turns the rule off.</summary>
    public int MinChecksToCount { get; set; } = 1;

    /// <summary>The most fires from one person on one author's looks that count in a week.</summary>
    public int MaxPerFirerPerAuthor { get; set; } = 3;

    /// <summary>Fires from accounts younger than this many days do not count.</summary>
    public int NewAccountDays { get; set; } = 2;

    /// <summary>Places on each board.</summary>
    public int Size { get; set; } = 10;

    /// <summary>The rising board is for accounts created within this many days.</summary>
    public int RisingDays { get; set; } = 30;

    /// <summary>The week's sponsor, when there is one (Board:Sponsor:Name and friends). Null when the section is absent.</summary>
    public BoardSponsorOptions? Sponsor { get; set; }
}

/// <summary>A brand that presents the week: a name, its handle in the app when it has one, the prize line and a link.</summary>
public sealed class BoardSponsorOptions
{
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public string PrizeText { get; set; } = "";
    public string Url { get; set; } = "";

    public bool Enabled => !string.IsNullOrWhiteSpace(Name);
}

/// <summary>
/// Store links leave the app through one door (/api/items/{id}/out) so they can be decorated, counted and revoked. Hosts
/// maps a host to the query string appended when a link goes there ("amazon.com" → "tag=orevosh-20"); a link to a host
/// that is not listed is redirected as given. Nothing is appended by default. Disclosure shows the commission line on
/// links that earn one.
/// </summary>
public sealed class AffiliateOptions
{
    public const string Section = "Affiliate";

    public bool Disclosure { get; set; } = true;

    /// <summary>Host (matched case-insensitively, subdomains included by the builder's rule) → query parameters, no leading "?".</summary>
    public Dictionary<string, string> Hosts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The parameters for a host, or null when the host earns nothing.</summary>
    public string? ParametersFor(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var key = host.Trim();
        // "www.amazon.com" and "smile.amazon.com" are the listed host's; "notamazon.com" is not.
        foreach (var (listed, parameters) in Hosts)
        {
            var name = listed.Trim();
            if (name.Length > 0 && !string.IsNullOrWhiteSpace(parameters)
                && (key.Equals(name, StringComparison.OrdinalIgnoreCase) || key.EndsWith("." + name, StringComparison.OrdinalIgnoreCase)))
            {
                return parameters.Trim().TrimStart('?', '&');
            }
        }

        return null;
    }
}
