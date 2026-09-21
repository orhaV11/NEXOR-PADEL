namespace FitCheck.Api.Domain;

/// <summary>
/// Round 16 — the money a FREE user can make. Everything else in this app is paid for by whoever made it; a tap on a
/// store link is the one thing that can pay for itself, and until this it left no trace beyond a single global tally
/// with no store, no item, no look and no day in it. Nothing that could be reconciled against what a partner reports,
/// and nothing that could tell the owner which looks earn.
/// <para>
/// No viewer is recorded. Who tapped is not needed to reconcile a commission, the privacy policy says what is kept,
/// and a row that names a person browsing shops is the kind of row that should not exist.
/// </para>
/// </summary>
public sealed class ItemClick
{
    public Guid Id { get; set; }

    /// <summary>The tagged item whose link was tapped. The item may be edited or deleted later; this row stays.</summary>
    public Guid ItemId { get; set; }

    /// <summary>The look it was tagged on, so "which looks earn" is answerable without joining through a deleted item.</summary>
    public Guid PostId { get; set; }

    /// <summary>
    /// Whose look it was. A commission earned through somebody's look is a fact about them — for a revenue share, for
    /// telling a brand which creator drove sales, and for the creator's own numbers.
    /// </summary>
    public Guid? OwnerId { get; set; }

    /// <summary>The store's host, lower-cased and without "www.": the key a partner's report is reconciled on.</summary>
    public string Host { get; set; } = "";

    /// <summary>
    /// Whether an affiliate programme was configured for that host AT THE MOMENT OF THE TAP. Stored rather than worked
    /// out later, because the configuration changes and a tap that could never have earned must not look, a month
    /// afterwards, like one that failed to.
    /// </summary>
    public bool Earning { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// What a partner says we earned. Two facts that are always confused and must not be: a commission is EXPECTED when
/// the partner first reports the sale, and CONFIRMED weeks later once the return window has closed. Some are never
/// confirmed — the buyer sent it back — and those are REVERSED. Counting expected money as income is how a business
/// spends what it does not have, so the state is on the row and the numbers page shows the three apart.
/// </summary>
public sealed class Commission
{
    public Guid Id { get; set; }

    /// <summary>The store's host, matching <see cref="ItemClick.Host"/>.</summary>
    public string Host { get; set; } = "";

    /// <summary>
    /// The partner's own id for the order or the action. Unique per host: a report re-imported, or the same sale
    /// reported again when it moves from expected to confirmed, updates the row it already made rather than adding a
    /// second one. Without this, every re-import doubles the revenue.
    /// </summary>
    public string ExternalId { get; set; } = "";

    /// <summary>The item and look it was matched to, when the partner's report carries enough to match. Often null.</summary>
    public Guid? ItemId { get; set; }
    public Guid? PostId { get; set; }

    /// <summary>Whose look earned it, copied from the click when one is matched.</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>
    /// The amount as the partner reports it, in hundredths of <see cref="Currency"/> — 4.20 is 420. Never converted
    /// between currencies here.
    /// <para>
    /// A long, not a decimal, for two reasons and the first one is not a preference: SQLite maps decimal to TEXT and
    /// cannot SUM it at all, so a report page that added money up threw a 500 the first time it was asked. The second
    /// is the older one — money held as a binary fraction drifts, and money ordered as text puts 9.00 after 10.00.
    /// </para>
    /// </summary>
    public long AmountMinor { get; set; }

    /// <summary>The same amount as money reads, for a DTO or a screen. Hundredths, so two decimal places.</summary>
    public decimal Amount => AmountMinor / 100m;

    /// <summary>Hundredths from what a partner reported, rounded half away from zero, which is how money rounds.</summary>
    public static long ToMinor(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    /// <summary>ISO 4217, upper-case. A partner reports in its own currency and the numbers page keeps them apart.</summary>
    public string Currency { get; set; } = "";

    /// <summary>One of <see cref="CommissionState"/>.</summary>
    public string State { get; set; } = CommissionState.Expected;

    /// <summary>When the sale happened, as the partner reports it — not when we imported it.</summary>
    public DateTime OccurredAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>The three things a commission can be. Strings, because they end up in reports and want to stay readable.</summary>
public static class CommissionState
{
    /// <summary>Reported, not yet safe. The return window is open and it may still vanish.</summary>
    public const string Expected = "expected";

    /// <summary>The partner has confirmed it. This is the only one that is income.</summary>
    public const string Confirmed = "confirmed";

    /// <summary>Returned, cancelled or rejected. Kept rather than deleted: a reversal is a fact worth seeing.</summary>
    public const string Reversed = "reversed";

    public static bool IsKnown(string? state) =>
        state is Expected or Confirmed or Reversed;
}
