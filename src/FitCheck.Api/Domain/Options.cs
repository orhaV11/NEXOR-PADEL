namespace FitCheck.Api.Domain;

public sealed class AnthropicOptions
{
    public const string Section = "Anthropic";

    /// <summary>Must support forced tool use (tool_choice type "tool"): Sonnet 5, Opus 5, the 4.x family, Haiku 4.5.</summary>
    public string Model { get; set; } = "claude-sonnet-5";

    /// <summary>
    /// The ceiling on ONE answer. It is not a charge: the bill is the tokens the model actually writes, so headroom is
    /// free and a ceiling that is too low is not. At 1200 a full verdict had almost none — the schema asks for a
    /// headline, a vibe, every garment with a note, what is working, the tip, a three-part breakdown and the accessories,
    /// and the model writes them in that order, so a cut lands the first few and silently drops the entire verdict. That
    /// is what a person saw as a score with nothing under it. Raised with room to spare, and a cut is now a failed
    /// answer that says so (AnthropicVisionClient) rather than half a screen.
    /// </summary>
    public int MaxTokens { get; set; } = 3000;

    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    // ---- Round 13 — money: what a call is estimated to cost (Services/SpendMeter.cs) ----

    /// <summary>
    /// USD per million input tokens. SET THIS TO YOUR CONTRACT'S PRICE. The default is the published list price for
    /// <see cref="Model"/>'s default (claude-sonnet-5) at the time of writing; a volume agreement, a different model or
    /// a price change makes it wrong, and every number on the money tiles and the daily ceiling is built on it.
    /// </summary>
    public decimal PriceInPerMillion { get; set; } = 2.00m;

    /// <summary>USD per million output tokens. SET THIS TO YOUR CONTRACT'S PRICE; see <see cref="PriceInPerMillion"/>.</summary>
    public decimal PriceOutPerMillion { get; set; } = 10.00m;
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
/// <summary>
/// The address the terms of use and the privacy policy tell a reader to write to. Those two pages promise a way to
/// reach a human — for a privacy question, for deleting an account's data, for reporting an account belonging to
/// someone under 16 — so the address in them has to be a mailbox that is actually read. It was a constant in the
/// translation files (<c>hello@orevosh.app</c>) until this, which is fine for whoever owns that domain and a dead
/// letterbox for everybody else who runs this code.
/// <para>
/// Unset, it falls back to <see cref="EmailOptions.From"/>: a server that sends mail has a mailbox by definition, and
/// the owner reads it. With neither set the client drops the contact section rather than print "write to us at .", and
/// <c>--doctor</c> says so.
/// </para>
/// </summary>
public sealed class LegalOptions
{
    public const string Section = "Legal";

    /// <summary>One address, shown to the public on two pages. Not a secret.</summary>
    public string ContactEmail { get; set; } = "";

    /// <summary>The address those pages should print, or null when this server has none.</summary>
    public static string? Contact(LegalOptions legal, EmailOptions email)
    {
        var configured = legal.ContactEmail.Trim();
        if (configured.Length > 0)
        {
            return configured;
        }

        // Email:From is often written as a display name around the address ("OREVOSH <hello@example.com>"), which is
        // right on an envelope and wrong in the middle of a sentence on a privacy page. Take the address out of it.
        var from = email.From.Trim();
        var open = from.LastIndexOf('<');
        var close = from.LastIndexOf('>');
        if (open >= 0 && close > open + 1)
        {
            from = from[(open + 1)..close].Trim();
        }

        return from.Length > 0 ? from : null;
    }
}

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

    /// <summary>
    /// Round 13 — money: the day's ceiling in estimated US dollars (UTC day). 0 is off, which is the default and what
    /// the doctor warns about. Above 0, once today's estimate (<see cref="Services.SpendMeter"/>, priced by
    /// <see cref="AnthropicOptions.PriceInPerMillion"/> and <see cref="AnthropicOptions.PriceOutPerMillion"/>) reaches
    /// it, every route that would ask the model answers 503 error.stylist_resting BEFORE the call and spends no
    /// allowance; it opens again at the next UTC midnight. <see cref="ChecksPerDayGlobal"/> stays as the count-based
    /// brake beside it: one caps how many calls are made, this one caps what they are estimated to cost.
    /// </summary>
    public decimal SpendPerDayUsd { get; set; }
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
    /// Round 16 - the month, which is the number that decides whether a subscription pays for itself. A daily cap of 30
    /// is a promise of 900 a month, and in Pro it is 1,980, because comparisons are a second bucket: at the measured
    /// cost of a model call that is about $40 of stylist for one subscriber, and up to $102 with the invite bonus. No
    /// consumer price survives that tail, and nobody real is anywhere near it - so the daily cap stays as the burst
    /// limit it always was, and this is the one that bounds the bill. Counted over a rolling 30 days, checks and
    /// comparisons together, both plans' buckets added up, because a bill does not care which bucket a call came from.
    /// 0 turns it off.
    /// </summary>
    public int ProCallsPerMonth { get; set; } = 150;

    /// <summary>The same for a free account. 0 (the default) leaves the free plan bounded by its day alone.</summary>
    public int FreeCallsPerMonth { get; set; }

    /// <summary>
    /// For a guest (no account yet), per guest cookie: what is counted is a stored check (ok, not_outfit or rejected, the
    /// ones that cost a model call), never a refused upload or a failed call.
    /// </summary>
    public int GuestChecksPerDay { get; set; } = 1;

    /// <summary>
    /// The same, per client ADDRESS rather than per cookie — and deliberately far above it, because an address is not a
    /// person. Behind a router, an office, a café or a carrier's CGNAT, everyone shares one, so holding this at
    /// <see cref="GuestChecksPerDay"/> meant showing the app to three friends and having two of them refused before they
    /// had taken a photo, told it was their own free look when their cookie had spent nothing. It stays well below
    /// <see cref="GuestAttemptsPerDay"/>: 1 &lt; 10 &lt; 20 is the ordering the docs promise. This is what stands between a
    /// cookieless script and the vision bill, and Limits:ChecksPerDayGlobal and Limits:SpendPerDayUsd still bound the day.
    /// </summary>
    public int GuestChecksPerAddressPerDay { get; set; } = 10;

    /// <summary>
    /// The abuse brake on the anonymous check path: attempts (whatever their outcome) per client address per day, in the
    /// "guest" rate-limit policy, answered 429 error.too_fast beyond it. Well above <see cref="GuestChecksPerDay"/> on
    /// purpose, so a refused photo or a model outage never locks a shared address out of its look.
    /// </summary>
    public int GuestAttemptsPerDay { get; set; } = 20;

    /// <summary>Shown on the Pro screen, e.g. "₪19 / month" or "$5 / month". Empty hides the price.</summary>
    public string ProPriceText { get; set; } = "";

    /// <summary>
    /// Round 16 - the price as a NUMBER, so every language sees it written the way that language writes money.
    /// <see cref="ProPriceText"/> is one string shown to everybody: "29.90 ILS / month" typed once reaches a Russian
    /// reader as "29.90 ILS / month", and a Hebrew reader with the shekel sign on the wrong side of the digits. The
    /// browser already knows all of this, so it is given the amount and the currency and asked to write it.
    /// 0 leaves the price off the page entirely, as an empty ProPriceText always did.
    /// </summary>
    public decimal ProPriceAmount { get; set; }

    /// <summary>The ISO 4217 code for <see cref="ProPriceAmount"/>, e.g. ILS, USD, EUR. Ignored when the amount is 0.</summary>
    public string ProPriceCurrency { get; set; } = "ILS";

    /// <summary>
    /// Round 16 - the price in other currencies: ISO 4217 code to amount, e.g.
    /// <c>Plans__ProPrices__USD=7.99</c>, <c>Plans__ProPrices__EUR=6.99</c>. A reader is shown the one for their
    /// region, falling back to <see cref="ProPriceCurrency"/> when their region has no price here.
    /// <para>
    /// These are PRICES, not conversions: nothing here multiplies by an exchange rate, because a subscription is
    /// priced per market and a rate that moved overnight must never move what a person is charged. Set each one
    /// deliberately, and set them to whatever Stripe will really charge in that currency - a page that shows one
    /// number and a checkout that takes another is the worst of the three possible outcomes.
    /// </para>
    /// </summary>
    public Dictionary<string, decimal> ProPrices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Which currency a region is priced in: ISO 3166 region to ISO 4217 code, e.g. <c>Plans__CurrencyByRegion__IL=ILS</c>.
    /// Merged over <see cref="DefaultCurrencyByRegion"/>, so only the corrections need setting.
    /// <para>
    /// The REGION, not the language. A Hebrew speaker in Berlin pays in euro and an English speaker in Tel Aviv pays
    /// in shekels; what someone reads in and what their bank works in are different facts, and the browser reports
    /// both. Language alone would have charged half the people in the app in a currency they do not hold.
    /// </para>
    /// </summary>
    public Dictionary<string, string> CurrencyByRegion { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Round 16 - what somebody reading from a country this server does not price in is shown. Empty means
    /// <see cref="ProPriceCurrency"/>, which is right for a home market and wrong for everywhere else: with the home
    /// currency as the only fallback, a reader in London or Sao Paulo meets a price in shekels. Setting this to USD
    /// makes the home market local and the rest of the world dollars, which is what most apps do and what the owner
    /// asked for. It needs a price of its own in <see cref="ProPrices"/> or it is ignored.
    /// </summary>
    public string ProPriceWorldCurrency { get; set; } = "";

    /// <summary>The currency for a region this server does not price: the world currency when it has a price, else the default one.</summary>
    public string FallbackCurrency()
    {
        var world = (ProPriceWorldCurrency ?? "").Trim().ToUpperInvariant();
        return world.Length == 3 && PriceTable().ContainsKey(world) ? world : (ProPriceCurrency ?? "").Trim().ToUpperInvariant();
    }

    /// <summary>
    /// The region-to-currency map every server starts with, so an owner sets prices and nothing else. Not exhaustive
    /// and not meant to be: a region that is missing, or whose currency has no price set, is shown the default one.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultCurrencyByRegion =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["IL"] = "ILS", ["PS"] = "ILS",
            ["US"] = "USD", ["EC"] = "USD", ["PA"] = "USD", ["SV"] = "USD",
            ["GB"] = "GBP", ["CH"] = "CHF", ["CA"] = "CAD", ["AU"] = "AUD", ["NZ"] = "NZD",
            ["RU"] = "RUB", ["BY"] = "BYN", ["UA"] = "UAH", ["KZ"] = "KZT", ["GE"] = "GEL", ["AM"] = "AMD",
            ["AE"] = "AED", ["SA"] = "SAR", ["EG"] = "EGP", ["MA"] = "MAD", ["JO"] = "JOD", ["QA"] = "QAR",
            ["KW"] = "KWD", ["BH"] = "BHD", ["OM"] = "OMR", ["LB"] = "LBP", ["TN"] = "TND", ["DZ"] = "DZD",
            ["IQ"] = "IQD", ["LY"] = "LYD", ["SD"] = "SDG", ["YE"] = "YER", ["SY"] = "SYP",
            ["TR"] = "TRY", ["PL"] = "PLN", ["CZ"] = "CZK", ["HU"] = "HUF", ["RO"] = "RON", ["BG"] = "BGN",
            ["SE"] = "SEK", ["NO"] = "NOK", ["DK"] = "DKK", ["IS"] = "ISK",
            ["IN"] = "INR", ["BR"] = "BRL", ["MX"] = "MXN", ["AR"] = "ARS", ["CL"] = "CLP", ["CO"] = "COP",
            ["JP"] = "JPY", ["CN"] = "CNY", ["KR"] = "KRW", ["SG"] = "SGD", ["HK"] = "HKD", ["TH"] = "THB",
            ["ID"] = "IDR", ["MY"] = "MYR", ["PH"] = "PHP", ["VN"] = "VND", ["ZA"] = "ZAR", ["NG"] = "NGN",
            // The euro, by country, because there is no "EU" region code on a browser's locale.
            ["AT"] = "EUR", ["BE"] = "EUR", ["CY"] = "EUR", ["DE"] = "EUR", ["EE"] = "EUR", ["ES"] = "EUR",
            ["FI"] = "EUR", ["FR"] = "EUR", ["GR"] = "EUR", ["HR"] = "EUR", ["IE"] = "EUR", ["IT"] = "EUR",
            ["LT"] = "EUR", ["LU"] = "EUR", ["LV"] = "EUR", ["MT"] = "EUR", ["NL"] = "EUR", ["PT"] = "EUR",
            ["SI"] = "EUR", ["SK"] = "EUR"
        };

    /// <summary>The map the client is given: the defaults with this server's own corrections over them.</summary>
    public Dictionary<string, string> RegionCurrencies()
    {
        var map = new Dictionary<string, string>(DefaultCurrencyByRegion, StringComparer.OrdinalIgnoreCase);
        foreach (var (region, currency) in CurrencyByRegion)
        {
            var code = (currency ?? "").Trim().ToUpperInvariant();
            if (region.Trim().Length > 0 && code.Length == 3)
            {
                map[region.Trim().ToUpperInvariant()] = code;
            }
        }

        return map;
    }

    /// <summary>Every currency this server has a price for, the default one included. Upper-cased, amounts above zero only.</summary>
    public Dictionary<string, decimal> PriceTable()
    {
        var table = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var fallback = (ProPriceCurrency ?? "").Trim().ToUpperInvariant();
        if (ProPriceAmount > 0 && fallback.Length == 3)
        {
            table[fallback] = ProPriceAmount;
        }

        foreach (var (currency, amount) in ProPrices)
        {
            var code = (currency ?? "").Trim().ToUpperInvariant();
            if (code.Length == 3 && amount > 0)
            {
                table[code] = amount;
            }
        }

        return table;
    }

    /// <summary>
    /// Round 13: how many "no outfit in this photo" answers a person (an account, or a guest cookie) gets back in the
    /// rolling day. Such an answer spent a model call and gave the person nothing, so the first ones are not counted
    /// against the plan cap, the guest's free look or me.checksToday; from the one after this number on they count like
    /// any stored check, so a stream of non-outfit photos still meets a cap. The global ceiling (Limits:ChecksPerDayGlobal)
    /// counts every one of them: it is about the bill. 0 makes every no-outfit answer count, as before Round 13.
    /// </summary>
    public int NoOutfitForgivenPerDay { get; set; } = 3;

    /// <summary>
    /// Whether "which one?" comparisons and the insights need Pro: POST /api/compare and GET /api/users/me/insights answer
    /// 403 error.pro_required to a free account, and the Pro page lists both as benefits only then. Off by default: Pro is
    /// a cap on a real cost, not a feature wall.
    /// </summary>
    public bool CompareNeedsPro { get; set; } = false;

    // ---------- Round 14 — Pro worth paying for, and the wardrobe ----------

    /// <summary>
    /// Round 14. A Pro account's comparisons get their OWN rolling-day allowance, counted apart from its checks, so
    /// deciding between two outfits never eats the day's checks — the thing people actually pay for. Never above
    /// <see cref="LimitsOptions.ChecksPerDay"/>, which stays the ceiling on either bucket. A FREE account is unchanged:
    /// one allowance for checks and comparisons together, as before (<see cref="Services.Spend"/>,
    /// <see cref="Services.Plans.CompareCapFor"/>). Not a promise of "unlimited": it is the fair-use brake on the
    /// comparison side, and <see cref="LimitsOptions.ChecksPerDayGlobal"/> and <see cref="LimitsOptions.SpendPerDayUsd"/>
    /// stand behind it as before.
    /// </summary>
    public int ProComparesPerDay { get; set; } = 30;

    /// <summary>
    /// Round 14 — the wardrobe. The most pieces one account may keep. A brake on a script, not a product limit: nobody
    /// dresses out of two hundred named pieces, and keeping is one tap with no form behind it. The same for free and Pro:
    /// the wardrobe itself is not what Pro sells (see <see cref="WardrobeNeedsPro"/>).
    /// </summary>
    public int WardrobeMaxItems { get; set; } = 200;

    /// <summary>
    /// Round 14 — what the wardrobe is FOR: how many of the wearer's own piece names travel with a check, so a tip can
    /// say "the brown tights you wore on the 4th" instead of "buy sheer brown tights". Most recently seen first, clothes
    /// only, each one short and cleaned like any other stored string (<see cref="Services.Wardrobe.PromptNames"/>).
    /// A handful is context; a hundred is noise and tokens. 0 turns the wardrobe off in the prompt entirely.
    /// </summary>
    public int WardrobeNamesToStylist { get; set; } = 12;

    /// <summary>
    /// Round 14: whether the wardrobe reaching the STYLIST is Pro's (the wardrobe itself is everyone's — it cannot build
    /// itself otherwise). True, and it is what Pro sells: a free account keeps, renames and deletes pieces and reads its
    /// own list, and POST /api/wardrobe/stylist answers 403 error.pro_required. False gives it to everyone, for a server
    /// that would rather not sell it.
    /// </summary>
    public bool WardrobeNeedsPro { get; set; } = true;

    /// <summary>
    /// Round 14: whether this server has the taste profile (the memory of what the person liked and turned down) built.
    /// It landed with the loop (Services/Taste.cs), so this is on: the Pro page may list it because the server can do it.
    /// Turn it off to take the benefit off that page and stop the advisory being built. The setting is the switch, not
    /// the feature — an account's own learning switch is theirs, and a guest never has one either way.
    /// </summary>
    public bool TasteProfile { get; set; } = true;

    /// <summary>
    /// Round 16: whether the taste profile reaching the stylist is PRO's, the way the wardrobe is (WardrobeNeedsPro).
    /// False - the default, and what this app has always done - gives it to every signed-in account, free included.
    /// <para>
    /// The distinction matters because the Pro page reads a flag per benefit and lists the benefit when the flag is
    /// true, and <see cref="TasteProfile"/> only ever meant "this server has the feature built". So "the stylist
    /// remembers you" was on the page while every free account already had it: a third of the pitch was something the
    /// reader could disprove by cancelling. The page now asks THIS flag, so the benefit is listed exactly when it is
    /// one. Turning it on takes something away from accounts that have it, which is a decision, not a default.
    /// </para>
    /// </summary>
    public bool TasteNeedsPro { get; set; }
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
    /// <summary>How long a computed week is served from memory, in seconds; 0 recomputes on every read (tests, tiny pilots).</summary>
    public int CacheSeconds { get; set; } = 60;

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

    /// <summary>
    /// The link as the board may show it: the trimmed <see cref="Url"/> when it is an absolute http(s) URL with a host
    /// and no user info, a bare host ("nexor.example", "www.nexor.example/drop") read as https, and null for anything
    /// else (another scheme, garbage), so a setting can never reach the page as a javascript: or a relative link. The
    /// spelling is otherwise kept as the owner wrote it.
    /// </summary>
    public static string? NormalizeUrl(string? url)
    {
        var text = (url ?? "").Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var colon = text.IndexOf(':');
        var hasScheme = colon > 0 && Uri.CheckSchemeName(text[..colon]);
        if (!hasScheme)
        {
            text = "https://" + text;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.Host.Length > 0
            && uri.UserInfo.Length == 0
            ? text
            : null;
    }
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

// ---- Round 13: languages shipped only when real ----

/// <summary>
/// Which UI languages are live (Languages:Enabled; <c>Languages__Enabled__0=en</c>, <c>__1=he</c> as environment
/// variables). Every locale the app knows (<see cref="Services.Localizer.SupportedLocales"/>: en, he, ar, ru) keeps its
/// files in the repository; only the ones listed here are offered by the client's switcher, auto-detected from the
/// browser, precomputed on /api/config and asked of the stylist. English is always in the list, since it is the fallback
/// for everything: a request for a language that is not enabled is answered in English and told nothing. The default is
/// English and Hebrew, the two the builders can read; Arabic and Russian are enabled with one setting once a native
/// reader has reviewed their files.
/// </summary>
public sealed class LanguagesOptions
{
    public const string Section = "Languages";

    public List<string> Enabled { get; set; } = ["en", "he"];

    /// <summary>
    /// The enabled locales as the app uses them: trimmed, lower-cased, only the locales the app knows, each once, in the
    /// order given, English first whatever the setting says (it is the fallback). A setting with nothing usable in it
    /// enables English alone.
    /// </summary>
    public IReadOnlyList<string> List
    {
        get
        {
            var list = new List<string> { Services.Localizer.DefaultLocale };
            foreach (var entry in Enabled ?? [])
            {
                var code = (entry ?? "").Trim().ToLowerInvariant();
                if (Services.Localizer.IsSupported(code) && !list.Contains(code))
                {
                    list.Add(code);
                }
            }

            return list;
        }
    }

    public bool IsEnabled(string? locale) => locale is not null && List.Contains(locale);

    /// <summary>The language the stylist is asked for: the one given when it is enabled, English otherwise.</summary>
    public string Effective(string? locale) => IsEnabled(locale) ? locale! : Services.Localizer.DefaultLocale;
}

// ---- Round 13 — money: the alerts the owner hears before a user does ----

/// <summary>
/// Where the app shouts when something is wrong, and how loud it may be (<see cref="Services.Alerter"/>). Both channels
/// are optional and independent: <see cref="Webhook"/> is an https URL that takes Slack/Discord-shaped JSON
/// (<c>{ "text": "..." }</c>), <see cref="Email"/> is one address that goes out through the app's own mail sender and so
/// only works while <see cref="EmailOptions.Enabled"/> is true. With neither set nothing is sent and every alert is only
/// a log line, which the doctor says plainly.
/// <para>
/// <b>Never a secret in an alert.</b> The texts name what happened, a number and a setting's NAME; never a key, a
/// password, a token or a link anyone could use. A webhook URL is itself a secret (whoever holds it can post to the
/// channel): it lives in the environment, never in appsettings, and the doctor prints only whether it is set.
/// </para>
/// </summary>
public sealed class AlertOptions
{
    public const string Section = "Alerts";

    /// <summary>
    /// An incoming-webhook URL that takes <c>{ "text": "..." }</c>: a Slack incoming webhook takes that shape as it is,
    /// and a Discord webhook URL with <c>/slack</c> appended takes the same one. Environment only
    /// (<c>Alerts__Webhook</c>), never appsettings. Empty means the channel is off.
    /// </summary>
    public string Webhook { get; set; } = "";

    /// <summary>One address that gets the same sentence, through <see cref="Services.IEmailSender"/>. Empty means off, and it sends nothing while mail is off.</summary>
    public string Email { get; set; } = "";

    /// <summary>More failed model calls than this within ten minutes raises the model alert. 0 turns that one alert off.</summary>
    public int ModelFailuresIn10Min { get; set; } = 5;

    /// <summary>Free space on the data volume below this many megabytes raises the disk alert. 0 turns that one alert off.</summary>
    public int DiskFreeMb { get; set; } = 512;

    /// <summary>Whether anything is sent at all; with both channels empty an alert is still logged.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(Webhook) || !string.IsNullOrWhiteSpace(Email);
}
