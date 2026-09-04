namespace FitCheck.Api.Domain;

public sealed class OutfitCheck
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public StyleIntent Intent { get; set; }

    /// <summary>Free text from the wearer, at most 120 characters.</summary>
    public string? Occasion { get; set; }

    /// <summary>Language the feedback was written in. Feedback is never re-displayed in another language.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Path relative to the storage root, or empty once the file has been removed.</summary>
    public string ImagePath { get; set; } = "";

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
