namespace FitCheck.Api.Domain;

public sealed class AppUser
{
    public Guid Id { get; set; }

    /// <summary>2–40 characters. Display only; the user id is the credential in Phase 1.</summary>
    public string Handle { get; set; } = "";

    /// <summary>Self-declared for the pilot. Real age assurance is required before public launch.</summary>
    public bool Confirmed16Plus { get; set; }

    /// <summary>BCP-47 tag of a shipped UI locale ("en", "he").</summary>
    public string PreferredLanguage { get; set; } = "en";

    public DateTime CreatedAt { get; set; }
}
