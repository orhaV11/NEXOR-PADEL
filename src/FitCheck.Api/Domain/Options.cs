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

    /// <summary>A look clip. The client records at most <see cref="MaxVideoSeconds"/>; the server enforces bytes only.</summary>
    public long MaxVideoBytes { get; set; } = 40 * 1024 * 1024;

    /// <summary>Advisory for the client (recording cap and the picker's duration check). Returned by /api/config.</summary>
    public int MaxVideoSeconds { get; set; } = 30;

    /// <summary>Re-encode clips to H.264 MP4 in the background when ffmpeg is available (WebM from Android cannot play on older iPhones).</summary>
    public bool Transcode { get; set; } = true;

    /// <summary>Path to the ffmpeg binary. Empty means look on PATH.</summary>
    public string FfmpegPath { get; set; } = "";
}

/// <summary>Outgoing mail for verification and password reset links. Host and From empty means mail is off: links are logged instead.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";

    /// <summary>Environment only (Email__Password), never appsettings.</summary>
    public string Password { get; set; } = "";

    public string From { get; set; } = "";
    public bool UseStartTls { get; set; } = true;

    /// <summary>The public https origin used in links, e.g. https://looks.example.com. Empty means the request's own origin.</summary>
    public string PublicOrigin { get; set; } = "";

    public bool Enabled => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>Web Push (VAPID). Both keys empty means push is off and the client never offers it.</summary>
public sealed class PushOptions
{
    public const string Section = "Push";

    /// <summary>Base64url uncompressed P-256 public key, as handed to pushManager.subscribe.</summary>
    public string PublicKey { get; set; } = "";

    /// <summary>Base64url P-256 private key. Environment variable Push__PrivateKey; never in appsettings.</summary>
    public string PrivateKey { get; set; } = "";

    /// <summary>mailto: or https: contact for the push services.</summary>
    public string Subject { get; set; } = "mailto:hello@orevosh.app";

    public bool Enabled => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}

/// <summary>
/// Handles promoted to moderator when the app starts (existing accounts only, see Data/AdminSync.cs) and kept from being
/// registered by anyone else. Case-insensitive. Environment variable Admin__Handles__0 etc. Being a moderator is the
/// persisted <see cref="AppUser.IsAdmin"/> flag; this list is only one way of setting it.
/// </summary>
public sealed class AdminOptions
{
    public const string Section = "Admin";

    public List<string> Handles { get; set; } = [];

    /// <summary>Whether the handle is on the list. Reserved at signup and promoted at start; nothing at request time reads it.</summary>
    public bool Lists(string handle) => Handles.Any(h => string.Equals(h.Trim(), handle, StringComparison.OrdinalIgnoreCase));
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
