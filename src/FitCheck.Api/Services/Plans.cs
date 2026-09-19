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

    private static int Clamp(int planCap, LimitsOptions limits) => Math.Max(0, Math.Min(planCap, limits.ChecksPerDay));
}
