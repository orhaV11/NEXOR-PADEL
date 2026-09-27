namespace FitCheck.Api.Domain;

/// <summary>
/// Round 19 — Tomorrow: one planned outfit, composed by the stylist from the pieces this account kept, and one stylist
/// call. Counted exactly where a check is counted (<see cref="Services.Spend"/> reads <see cref="Status"/> and
/// <see cref="CreatedAt"/> the way it reads a check's), so the day, the month and the global ceiling all see it and
/// nothing about it is free. Deliberately no unique key: the bound is the counted row, not a key.
/// <para>
/// What the row holds is what the person is shown and what the numbers page reads. What it never holds: where the
/// person was. The forecast figures the outfit was composed for are here; the coordinates that fetched them were
/// rounded, handed to the forecast and dropped.
/// </para>
/// </summary>
public sealed class OutfitSuggestion
{
    public Guid Id { get; set; }

    /// <summary>The account. There is no guest suggestion: a guest has no wardrobe.</summary>
    public Guid UserId { get; set; }

    /// <summary>Where the outfit is going: the cache key and the label.</summary>
    public OutfitOccasion Occasion { get; set; }

    /// <summary>How they want it to read, or null: the cache key too.</summary>
    public OutfitStyle? Style { get; set; }

    /// <summary>The one-word value of the pair, <see cref="StyleIntents.Legacy"/>, as every check carries.</summary>
    public StyleIntent Intent { get; set; }

    /// <summary>"today" or "tomorrow": the label the person chose.</summary>
    public string When { get; set; } = "tomorrow";

    /// <summary>The calendar day it dresses, in the person's own calendar; the cache key.</summary>
    public DateOnly ForDate { get; set; }

    /// <summary>The sentence's language. Never re-shown in another one, the feedback rule.</summary>
    public string Language { get; set; } = "en";

    /// <summary>1 for the first answer to a key on that day, n+1 for "another idea"; the screen says "Idea 2".</summary>
    public int Seq { get; set; } = 1;

    /// <summary>One of <see cref="CheckStatus"/>: ok, rejected or error. Never not_outfit. Error is stored and not counted.</summary>
    public string Status { get; set; } = CheckStatus.Error;

    /// <summary>What the model wrote, or the localized template when its sentence was filtered. Empty for rejected and error rows.</summary>
    public string Sentence { get; set; } = "";

    /// <summary>Whether the template replaced the model's sentence (a metric).</summary>
    public bool SentenceTemplated { get; set; }

    /// <summary>The one kind of piece the wardrobe lacks for this day, one of <see cref="Services.Wardrobe.PromptCategories"/>, or null.</summary>
    public string? Gap { get; set; }

    /// <summary>How many pieces the model was shown (12 free, 40 Pro), for the metrics and for reading old rows honestly.</summary>
    public int PiecesOffered { get; set; }

    /// <summary>Refs the model returned that were not in its list and were dropped in code (a metric).</summary>
    public int InventedRefs { get; set; }

    /// <summary>Times this row was handed back from the cache instead of a new call (a metric: the cache-hit rate).</summary>
    public int Reuses { get; set; }

    public bool TasteUsed { get; set; }

    public bool WeatherUsed { get; set; }

    /// <summary>The forecast the outfit was composed for, when there was one. Never a place.</summary>
    public double? WeatherTempMaxC { get; set; }
    public double? WeatherTempMinC { get; set; }
    public int? WeatherPrecipChance { get; set; }
    public int? WeatherCode { get; set; }

    public string PromptVersion { get; set; } = "";

    public int LatencyMs { get; set; }

    /// <summary>What <see cref="Services.Spend"/> counts.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The person's own verdict, byte for byte the check's four columns (<see cref="OutfitCheck.Useful"/> and the
    /// three beside it), so <see cref="Services.Taste"/> reads one shape from two tables.
    /// </summary>
    public bool? Useful { get; set; }
    public DateTime? UsefulAt { get; set; }
    public string? UsefulNote { get; set; }
    public string? UsefulReason { get; set; }

    /// <summary>The check that closed the loop: they wore it and checked it (<see cref="OutfitCheck.SuggestionId"/> points back).</summary>
    public Guid? WornCheckId { get; set; }
}

/// <summary>
/// One piece of a planned outfit, in the model's order. <see cref="Name"/> and <see cref="Category"/> are copied at
/// compose time so history reads right after a rename, and <see cref="ItemId"/> goes null (never the row) when the
/// piece is removed from the wardrobe. <see cref="PhotoCheckId"/> is the check whose photo shows the person wearing
/// it, chosen in code and never by the model.
/// </summary>
public sealed class SuggestionPiece
{
    public Guid SuggestionId { get; set; }

    public int Position { get; set; }

    public Guid? ItemId { get; set; }

    public string Name { get; set; } = "";

    public string Category { get; set; } = "";

    public Guid? PhotoCheckId { get; set; }
}
