namespace FitCheck.Api.Domain;

/// <summary>
/// Round 16 — the month, written back to the person. A subscription needs a reason that is worth more in month six
/// than in month one, and this is the other one beside the wardrobe: nobody remembers what they were told in March,
/// and the app does.
/// <para>
/// One per account per calendar month per language, minted the first time it is asked for and then read from here.
/// That shape is the cost control: a recap costs one TEXT-ONLY model call — no photograph, so about half a cent —
/// and a subscriber who never opens the page costs nothing at all, which a scheduled job could not manage.
/// </para>
/// <para>
/// The numbers in it are computed by <see cref="Endpoints.InsightsEndpoints.Compute"/> before the model is asked
/// anything. The model is given those numbers and told to write them as sentences; it is never asked what the numbers
/// are. A recap that invented a figure about somebody's own year would be worse than no recap.
/// </para>
/// </summary>
public sealed class Recap
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The first day of the calendar month it covers, in UTC, at midnight. The key, with the account and language.</summary>
    public DateTime Month { get; set; }

    /// <summary>The language it was written in. Someone who switches language gets their own, rather than last month's in the wrong one.</summary>
    public string Language { get; set; } = "";

    /// <summary>Two to four sentences. Plain text: it is read, not parsed.</summary>
    public string Text { get; set; } = "";

    /// <summary>How many checks it was written from, so a thin month can be told from a full one afterwards.</summary>
    public int Checks { get; set; }

    public DateTime CreatedAt { get; set; }
}
