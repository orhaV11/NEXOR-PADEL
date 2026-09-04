namespace FitCheck.Api.Domain;

public sealed class AppUser
{
    public Guid Id { get; set; }

    /// <summary>2–40 characters, letters, digits, dot and underscore. Unique, case-insensitive.</summary>
    public string Handle { get; set; } = "";

    /// <summary>Lower-cased copy of the handle for the unique index and for lookups.</summary>
    public string HandleLower { get; set; } = "";

    /// <summary>ASP.NET Core PasswordHasher output. Never exposed.</summary>
    public string PasswordHash { get; set; } = "";

    public AccountType AccountType { get; set; }

    /// <summary>Shown instead of the handle when set. At most 40 characters.</summary>
    public string? DisplayName { get; set; }

    /// <summary>At most 160 characters.</summary>
    public string? Bio { get; set; }

    /// <summary>Brands mostly. https only.</summary>
    public string? Website { get; set; }

    /// <summary>Self-declared for the pilot. Real age assurance is required before public launch.</summary>
    public bool Confirmed16Plus { get; set; }

    /// <summary>BCP-47 tag of a shipped UI locale ("en", "he").</summary>
    public string PreferredLanguage { get; set; } = "en";

    /// <summary>Consecutive days (UTC) with at least one ok check.</summary>
    public int StreakCount { get; set; }

    /// <summary>UTC date of the latest ok check, midnight.</summary>
    public DateTime? LastCheckDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Handle : DisplayName!;
}
