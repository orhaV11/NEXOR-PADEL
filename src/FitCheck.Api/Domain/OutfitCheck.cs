namespace FitCheck.Api.Domain;

public sealed class OutfitCheck
{
    public Guid Id { get; set; }

    /// <summary>The owner, or null while the check belongs to a guest (see <see cref="GuestToken"/>).</summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// A guest's check: the random token from the guest cookie that made it. Signing up with that cookie claims the
    /// check (UserId set, token cleared, ClaimedAt stamped); unclaimed guest checks and their photos expire within a day.
    /// </summary>
    public string? GuestToken { get; set; }

    public DateTime? ClaimedAt { get; set; }

    /// <summary>
    /// Round 14: the ONE-WORD value for this check, <see cref="StyleIntents.Legacy"/> of the pair below. Kept because a
    /// look, a board, a challenge, the feed filter and the interests list all speak it, and because every surface with
    /// room for a single word shows it. Written on every check; never the thing the stylist is asked about.
    /// </summary>
    public StyleIntent Intent { get; set; }

    /// <summary>Round 14: where the outfit is going. Chosen on every check; the score is always relative to it.</summary>
    public OutfitOccasion Occasion { get; set; }

    /// <summary>
    /// Round 14: how the wearer wants the look to read. Null is a first-class answer — no style asked for, so the look is
    /// judged on its own terms for the occasion, which is what Date, Office, Party and Sport always did.
    /// </summary>
    public OutfitStyle? Style { get; set; }

    /// <summary>
    /// Free text from the wearer, at most 120 characters: the detail no chip can hold ("my cousin's wedding, outdoors").
    /// Round 14 renamed it from Occasion, which is now the chip; older clients still send it as "occasion".
    /// </summary>
    public string? Note { get; set; }

    /// <summary>Language the feedback was written in. Feedback is never re-displayed in another language.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Path relative to the storage root, or empty once the file has been removed.</summary>
    public string ImagePath { get; set; } = "";

    /// <summary>
    /// A short clip of the same look (MP4 or WebM), relative to the storage root, when the check came from a clip. The
    /// stylist judged the still in <see cref="ImagePath"/>, which is also the clip's poster. Null for photo checks and
    /// once the file has been removed.
    /// </summary>
    public string? VideoPath { get; set; }

    /// <summary>Round 13: the person's own verdict on the verdict — true "the tip was right", false "it missed" — with when,
    /// and an optional short note. Null until they say. The only signal that measures whether the stylist is any good.</summary>
    public bool? Useful { get; set; }
    public DateTime? UsefulAt { get; set; }
    public string? UsefulNote { get; set; }

    /// <summary>
    /// Round 14 — the loop: which of the four typed answers they gave (<see cref="TipReason"/>), beside the yes/no above,
    /// because "I tried it and it worked", "it did not", "that is not my style" and "I do not own that" are four different
    /// facts and only the typed one can teach anything. Null for a check answered before Round 14, or not answered at all;
    /// <see cref="Useful"/> stays the yes/no behind it (only <c>worked</c> is a yes).
    /// </summary>
    public string? UsefulReason { get; set; }

    /// <summary>One of <see cref="CheckStatus"/>.</summary>
    public string Status { get; set; } = CheckStatus.Error;

    public int? Score { get; set; }

    /// <summary>Serialized <see cref="OutfitFeedback"/> (camelCase, same shape the API returns). Null unless status is ok or not_outfit.</summary>
    public string? FeedbackJson { get; set; }

    /// <summary>Rubric version that produced this check, so score distributions can be compared across prompt changes.</summary>
    public string PromptVersion { get; set; } = "";

    public int LatencyMs { get; set; }
    public DateTime CreatedAt { get; set; }
}
