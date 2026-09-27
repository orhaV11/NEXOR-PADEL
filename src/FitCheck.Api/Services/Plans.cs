using FitCheck.Api.Domain;

namespace FitCheck.Api.Services;

/// <summary>
/// What a plan allows. Pro is a flag plus an end date, so a lapsed subscription falls back to free by itself; the
/// billing builder sets both, the --pro command sets them by hand.
/// </summary>
public static class Plans
{
    public const string Free = "free";
    public const string Pro = "pro";

    public static bool IsPro(AppUser user, DateTime now) =>
        string.Equals(user.Plan, Pro, StringComparison.OrdinalIgnoreCase) && (user.ProUntil is null || user.ProUntil > now);

    /// <summary>Checks (and comparisons) per rolling day for this account: the plan's cap, never above Limits:ChecksPerDay.</summary>
    public static int CapFor(AppUser user, PlanOptions plans, LimitsOptions limits, DateTime now) =>
        IsPro(user, now) ? ProCap(plans, limits) : Clamp(plans.FreeChecksPerDay, limits);

    /// <summary>
    /// What Pro really gets: Plans:ProChecksPerDay, never above Limits:ChecksPerDay. The number to publish and to promise
    /// (the Pro page, the cap message), so a ceiling below the plan's cap is never advertised as more than it pays for.
    /// </summary>
    public static int ProCap(PlanOptions plans, LimitsOptions limits) => Clamp(plans.ProChecksPerDay, limits);

    /// <summary>
    /// Round 16 - how many model calls this account may make in a rolling month, 0 when the plan has no monthly bound.
    /// The day is the burst limit; this is the one that decides whether the subscription pays for itself.
    /// </summary>
    public static int MonthlyCallsFor(AppUser user, PlanOptions plans, DateTime now) =>
        Math.Max(0, IsPro(user, now) ? plans.ProCallsPerMonth : plans.FreeCallsPerMonth);

    private static int Clamp(int planCap, LimitsOptions limits) => Math.Max(0, Math.Min(planCap, limits.ChecksPerDay));

    // ---------- Round 14 — Pro worth paying for: what the plan gets, not how high the cap goes ----------

    /// <summary>
    /// Which bucket a caller's checks are counted in. A free account keeps one allowance for checks and comparisons
    /// together, exactly as before Round 14; a Pro account counts its checks apart from its comparisons, so the two
    /// cannot eat each other. Guests are always <see cref="Allowance.Together"/>: they have one look, not two buckets.
    /// </summary>
    public static Allowance CheckAllowanceFor(AppUser? user, DateTime now) =>
        user is not null && IsPro(user, now) ? Allowance.Checks : Allowance.Together;

    /// <summary>The bucket a caller's comparisons are counted in; the other half of <see cref="CheckAllowanceFor"/>.</summary>
    public static Allowance CompareAllowanceFor(AppUser? user, DateTime now) =>
        user is not null && IsPro(user, now) ? Allowance.Compares : Allowance.Together;

    /// <summary>
    /// Comparisons per rolling day for this account. Pro has its own: Plans:ProComparesPerDay, never above
    /// Limits:ChecksPerDay, counted apart from its checks — the thing Pro sells. A free account shares the one
    /// allowance with its checks, so this is <see cref="CapFor"/> for it, unchanged from before Round 14.
    /// </summary>
    public static int CompareCapFor(AppUser user, PlanOptions plans, LimitsOptions limits, DateTime now) =>
        IsPro(user, now) ? ProCompareCap(plans, limits) : CapFor(user, plans, limits, now);

    /// <summary>What a Pro account's comparison allowance really is (clamped to Limits:ChecksPerDay): the number to publish.</summary>
    public static int ProCompareCap(PlanOptions plans, LimitsOptions limits) => Clamp(plans.ProComparesPerDay, limits);

    /// <summary>
    /// Whether this account's own wardrobe may travel with its checks, so a tip can name a piece it already owns.
    /// The wardrobe itself is everyone's; this is what Pro buys (Plans:WardrobeNeedsPro). A guest has no wardrobe.
    /// </summary>
    public static bool WardrobeReachesStylist(AppUser? user, PlanOptions plans, DateTime now) =>
        user is not null && (!plans.WardrobeNeedsPro || IsPro(user, now));

    /// <summary>
    /// Round 20 — the Pro moment: whether "your wardrobe has N pieces; Pro lets the stylist see them" is a true sentence
    /// for this account right now. It is only while the wardrobe is Pro's on this server (Plans:WardrobeNeedsPro), the
    /// account is free, the free slice is above zero, and the wardrobe holds strictly MORE than that slice
    /// (Plans:WardrobeNamesToStylist): at thirteen pieces "the stylist does not see all of them" is a fact about this
    /// server, at twelve it is not. The same predicates <see cref="Services.Wardrobe.ForStylistAsync"/> decides by, so
    /// the sentence can never promise more than the server keeps.
    /// </summary>
    public static bool WardrobeProMoment(AppUser user, PlanOptions plans, DateTime now, int count) =>
        plans.WardrobeNeedsPro && !IsPro(user, now) && plans.WardrobeNamesToStylist > 0 && count > plans.WardrobeNamesToStylist;

    // ---------- Round 19 — Tomorrow ----------

    /// <summary>
    /// Whether this account may ask for an outfit at all: the feature is on, they are signed in (a guest has no
    /// wardrobe), and either it is not Pro's or they are Pro. The caps below decide how many.
    /// </summary>
    public static bool TomorrowReachesStylist(AppUser? user, PlanOptions plans, DateTime now) =>
        plans.TomorrowEnabled && user is not null && (!plans.TomorrowNeedsPro || IsPro(user, now));

    /// <summary>A Pro account's own day of planned outfits, never above Limits:ChecksPerDay.</summary>
    public static int ProSuggestionCap(PlanOptions plans, LimitsOptions limits) => Clamp(plans.ProSuggestionsPerDay, limits);

    /// <summary>How many of a free account's shared day may be planned outfits: the brake, never above the day itself.</summary>
    public static int FreeSuggestionCap(PlanOptions plans, LimitsOptions limits) =>
        Math.Max(0, Math.Min(plans.FreeSuggestionsPerDay, Clamp(plans.FreeChecksPerDay, limits)));

    /// <summary>Pro counts its planned outfits on their own; free counts them with everything else it did today.</summary>
    public static Allowance SuggestionAllowanceFor(AppUser user, DateTime now) =>
        IsPro(user, now) ? Allowance.Suggestions : Allowance.Together;
}

/// <summary>
/// Round 14: which stored calls a daily allowance counts. <see cref="Together"/> is the one bucket a free account and a
/// guest have always had; Pro splits its day in two so that deciding between two outfits never spends a check.
/// </summary>
public enum Allowance
{
    /// <summary>Checks and comparisons in one list: a free account, a guest.</summary>
    Together,

    /// <summary>Checks only: a Pro account's check allowance.</summary>
    Checks,

    /// <summary>Comparisons only: a Pro account's comparison allowance.</summary>
    Compares,

    /// <summary>Round 19 — suggestions only: a Pro account's Tomorrow allowance, apart from its checks and its comparisons.</summary>
    Suggestions
}
