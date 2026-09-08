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

    /// <summary>Three sub-scores behind the overall score (rubric v2). Null for checks made before it existed.</summary>
    public ScoreBreakdown? Breakdown { get; set; }

    /// <summary>The accessories read on their own (rubric v2). Null for checks made before it existed.</summary>
    public AccessoriesFeedback? Accessories { get; set; }

    /// <summary>Only when status is not ok: short, friendly explanation.</summary>
    public string? Message { get; set; }
}

/// <summary>1–10 each: how the garments are cut and sit, the palette, and what finishes the look.</summary>
public sealed class ScoreBreakdown
{
    public int Fit { get; set; }
    public int Color { get; set; }
    public int Accessories { get; set; }
}

/// <summary>Jewelry, bags, belts, hats, glasses, watches, scarves, visible socks: the pieces that finish a look.</summary>
public sealed class AccessoriesFeedback
{
    /// <summary>adds | neutral | missing | clashes</summary>
    public string Verdict { get; set; } = "neutral";

    /// <summary>What the stylist saw, e.g. "gold hoops", "black leather belt".</summary>
    public List<string> Present { get; set; } = [];

    public string Note { get; set; } = "";

    /// <summary>The one accessory that would finish this look for this intent, doable with common pieces.</summary>
    public string AddOne { get; set; } = "";
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

/// <summary>The stylist's answer to "which one?": both outfits scored for the same intent, a winner, and why in one breath.</summary>
public sealed class ComparisonFeedback
{
    public string Status { get; set; } = CheckStatus.Ok;

    /// <summary>a | b</summary>
    public string Winner { get; set; } = "a";

    public int ScoreA { get; set; }
    public int ScoreB { get; set; }
    public string HeadlineA { get; set; } = "";
    public string HeadlineB { get; set; } = "";

    /// <summary>Two or three sentences: what decides it, for this intent.</summary>
    public string Reason { get; set; } = "";

    /// <summary>One change that would make the loser win, or the winner better.</summary>
    public string OneTip { get; set; } = "";

    /// <summary>Only when status is not ok.</summary>
    public string? Message { get; set; }
}
