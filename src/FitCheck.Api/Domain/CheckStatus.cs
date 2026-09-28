namespace FitCheck.Api.Domain;

/// <summary>Stored as plain strings so the SQLite rows stay readable without the enum.</summary>
public static class CheckStatus
{
    public const string Ok = "ok";
    public const string NotOutfit = "not_outfit";
    public const string Rejected = "rejected";
    public const string Error = "error";
}
