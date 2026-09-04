namespace FitCheck.Api.Domain;

public sealed class AnthropicOptions
{
    public const string Section = "Anthropic";

    /// <summary>Must support forced tool use (tool_choice type "tool"): Sonnet 5, Opus 5, the 4.x family, Haiku 4.5.</summary>
    public string Model { get; set; } = "claude-sonnet-5";

    public int MaxTokens { get; set; } = 1200;

    public string BaseUrl { get; set; } = "https://api.anthropic.com";
}

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Directory for private photos. Relative paths resolve against the content root, never wwwroot.</summary>
    public string Root { get; set; } = "storage";

    public long MaxImageBytes { get; set; } = 6 * 1024 * 1024;
}

public sealed class LimitsOptions
{
    public const string Section = "Limits";

    /// <summary>Per-user cap over a rolling 24 hours. Cost control, not a product feature.</summary>
    public int ChecksPerDay { get; set; } = 20;

    /// <summary>Ceiling across all users over a rolling 24 hours, so a leaked URL cannot run up an unbounded bill.</summary>
    public int ChecksPerDayGlobal { get; set; } = 1000;

    /// <summary>New accounts per client address per hour. Blunts minting fresh ids to dodge the per-user cap.</summary>
    public int SignupsPerHourPerIp { get; set; } = 50;

    /// <summary>Login attempts per client address per 15 minutes. Slows password guessing.</summary>
    public int LoginsPerQuarterHourPerIp { get; set; } = 30;

    /// <summary>Reports that hide a post pending review.</summary>
    public int ReportsToHide { get; set; } = 3;
}
