namespace FitCheck.Api.Domain;

public sealed class AppUser
{
    public Guid Id { get; set; }

    /// <summary>2–40 characters, letters, digits, dot and underscore. Unique, case-insensitive.</summary>
    public string Handle { get; set; } = "";

    /// <summary>Lower-cased copy of the handle for the unique index and for lookups.</summary>
    public string HandleLower { get; set; } = "";

    /// <summary>ASP.NET Core PasswordHasher output. Never exposed.</summary>
    public string PasswordHash { get; set; } = "";

    public AccountType AccountType { get; set; }

    /// <summary>Shown instead of the handle when set. At most 40 characters.</summary>
    public string? DisplayName { get; set; }

    /// <summary>At most 160 characters.</summary>
    public string? Bio { get; set; }

    /// <summary>Brands mostly. https only.</summary>
    public string? Website { get; set; }

    /// <summary>Profile photo under the private storage root (&lt;userId&gt;/avatar.&lt;ext&gt;). Served only through the avatar route.</summary>
    public string? AvatarPath { get; set; }

    /// <summary>Bumped on every upload and put in the avatar URL, so caches refresh without cache-busting headers.</summary>
    public int AvatarVersion { get; set; }

    /// <summary>Comma-separated StyleIntent names the person picked at onboarding or in settings. At most one of each.</summary>
    public string? Interests { get; set; }

    /// <summary>Self-declared for the pilot. Real age assurance is required before public launch.</summary>
    public bool Confirmed16Plus { get; set; }

    /// <summary>BCP-47 tag of a shipped UI locale ("en", "he").</summary>
    public string PreferredLanguage { get; set; } = "en";

    /// <summary>Consecutive days (UTC) with at least one ok check.</summary>
    public int StreakCount { get; set; }

    /// <summary>UTC date of the latest ok check, midnight.</summary>
    public DateTime? LastCheckDate { get; set; }

    /// <summary>Optional, for account recovery only. Lower-cased; unique among accounts that have one. Never shown to others.</summary>
    public string? Email { get; set; }

    /// <summary>Set when the person opened the verification link for the current address; cleared when the address changes.</summary>
    public DateTime? EmailVerifiedAt { get; set; }

    /// <summary>free | pro. Pro raises the daily check cap and unlocks the comparison and the insights; see PlanOptions.</summary>
    public string Plan { get; set; } = "free";

    /// <summary>When the paid period ends (null for free). Checked on every request that needs Pro.</summary>
    public DateTime? ProUntil { get; set; }

    /// <summary>The billing provider's customer id, when a checkout ever happened. Never shown.</summary>
    public string? BillingCustomerId { get; set; }

    /// <summary>
    /// The subscription Checkout opened (sub_…, ≤ 64), so the webhook can tell the subscription that was paid for from
    /// any other on the same customer (Round 11). Null until the billing builder stores it from checkout.session.completed
    /// (the session's "subscription") and compares it in customer.subscription.deleted/updated (the object's "id"); see the
    /// TODOs in BillingEndpoints. Cleared when that subscription ends. Never shown.
    /// </summary>
    public string? BillingSubscriptionId { get; set; }

    /// <summary>Date of birth from signup (UTC date). Sixteen and over only; earlier accounts have null and their checkbox.</summary>
    public DateTime? BirthDate { get; set; }

    /// <summary>A brand confirmed by hand: the --verify command or a moderator on #/admin. Shown as a check next to the brand mark.</summary>
    public bool Verified { get; set; }

    /// <summary>Set by an admin. A suspended account cannot sign in and its looks are hidden until it is lifted.</summary>
    public bool Suspended { get; set; }

    /// <summary>Round 13: the weekly digest mail is on unless the person turned it off in Settings; when the last one went.</summary>
    public bool DigestOn { get; set; } = true;
    public DateTime? LastDigestAt { get; set; }

    /// <summary>Round 13: who invited this account (an invite link carrying a handle), for the referral bonus and the numbers page.</summary>
    public Guid? InvitedByUserId { get; set; }

    /// <summary>Round 20: the entry link (/go/&lt;source&gt;) the device arrived through before this signup, an allowlisted word or null.</summary>
    public string? Source { get; set; }

    /// <summary>
    /// Round 20: the charge the last pre-renewal recap mail was written for; a renewal moves the charge on, which makes it
    /// once per period. Round 20 wrote the ProUntil here, which is later than its own charge, so an old stamp still
    /// covers its period.
    /// </summary>
    public DateTime? RenewalRecapUntil { get; set; }

    /// <summary>
    /// Review of Round 20: whether the followed subscription will charge again at <see cref="BillingPeriodEnd"/>, as the
    /// last webhook event said: false once it is set to cancel at the period end, while it trials with no card on it, and
    /// while collection has stopped (past due, unpaid, paused). Null until Stripe says (an account from before the
    /// column), read as renewing, as before. Only the renewal recap reads it. Never shown.
    /// </summary>
    public bool? BillingRenews { get; set; }

    /// <summary>
    /// Review of Round 20: the end of the followed subscription's current period, the moment Stripe charges next: the
    /// period a completed Checkout sold (a month, a year or the trial's days from then) until an event names Stripe's own
    /// (a subscription's current_period_end, a paid invoice's lines). Null for an account from before the column until
    /// the next such event. Unlike <see cref="ProUntil"/> it carries no slack and no gift. Never shown.
    /// </summary>
    public DateTime? BillingPeriodEnd { get; set; }

    /// <summary>Round 20: the person's own switch for the morning push (Settings); the server flag decides whether it is offered at all.</summary>
    public bool TomorrowPushOn { get; set; } = true;

    /// <summary>Round 20: a moderator keeps this account's looks off every board while set; the audit line in the log is the record of who and why.</summary>
    public DateTime? BoardExcludedAt { get; set; }

    /// <summary>
    /// A moderator. Persisted, never derived from the handle at request time: the cookie carries a handle, and a handle is
    /// something anyone can register once it is free. Set by the start-up sync for the handles in Admin:Handles (existing
    /// accounts only, never demoted) and by the <c>--admin</c> / <c>--unadmin</c> commands; see Data/AdminSync.cs.
    /// </summary>
    public bool IsAdmin { get; set; }

    public DateTime CreatedAt { get; set; }

    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Handle : DisplayName!;
}
