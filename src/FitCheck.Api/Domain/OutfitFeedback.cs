namespace FitCheck.Api.Domain;

/// <summary>
/// Structured feedback as filled by the model's forced tool call.
/// Property names are mapped to snake_case when reading the tool payload and to camelCase in API responses.
/// </summary>
public sealed class OutfitFeedback
{
    public string Status { get; set; } = CheckStatus.Ok;

    /// <summary>1–10, relative to the stated intent.</summary>
    public int Score { get; set; }

    /// <summary>0–100.</summary>
    public int IntentMatch { get; set; }

    public string Headline { get; set; } = "";
    public string Vibe { get; set; } = "";
    public List<OutfitItem> Items { get; set; } = [];
    public List<string> Working { get; set; } = [];
    public string OneTip { get; set; } = "";

    /// <summary>Only when status is not ok: short, friendly explanation.</summary>
    public string? Message { get; set; }
}

public sealed class OutfitItem
{
    public string Name { get; set; } = "";

    /// <summary>top | bottom | dress | outerwear | shoes | accessory | other</summary>
    public string Category { get; set; } = "other";

    /// <summary>works | neutral | weak</summary>
    public string Verdict { get; set; } = "neutral";

    public string Note { get; set; } = "";
}
