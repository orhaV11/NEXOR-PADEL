using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>
/// One-time links for email verification and password reset. A token is 32 random bytes as base64url; only its SHA-256
/// (base64url too) is stored, so a copy of the database cannot mint a link. Issuing a token voids the open ones of the
/// same purpose for that user, a token is spent on first use, and expiry is 24 hours for a verification and one hour for
/// a reset. Also the one rule for what counts as an email address on an account.
/// </summary>
public static partial class RecoveryTokens
{
    public static readonly TimeSpan VerifyLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);
    public const int MaxEmailLength = 200;

    // 32 bytes as base64url without padding: always 43 characters of this alphabet. Anything else is refused before
    // the database is asked.
    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex TokenRegex();

    // MailAddress accepts a lot ("a@b", quoted local parts); this keeps addresses to the shape a mail server will route.
    [GeneratedRegex(@"^[^\s@""<>()]+@[^\s@""<>()]+\.[^\s@""<>().]{2,}$")]
    private static partial Regex EmailRegex();

    public static string Mint() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    /// <summary>
    /// Voids every open token of this purpose for the user and adds a fresh one to the change tracker (the caller saves,
    /// so the token lands in the same transaction as the change it belongs to). Returns the token the person receives.
    /// </summary>
    public static async Task<string> IssueAsync(AppDbContext db, Guid userId, string purpose, string? email, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await VoidOpenAsync(db, userId, purpose, now, ct);

        var token = Mint();
        db.AuthTokens.Add(new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Purpose = purpose,
            TokenHash = Hash(token),
            Email = email,
            ExpiresAt = now + (purpose == AuthTokenPurpose.Reset ? ResetLifetime : VerifyLifetime),
            CreatedAt = now
        });
        return token;
    }

    /// <summary>Marks every open token of this purpose used, through the change tracker: the caller saves.</summary>
    public static async Task VoidOpenAsync(AppDbContext db, Guid userId, string purpose, DateTime now, CancellationToken ct)
    {
        var open = await db.AuthTokens.Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null).ToListAsync(ct);
        foreach (var token in open)
        {
            token.UsedAt = now;
        }
    }

    /// <summary>
    /// The open, unexpired token row this string stands for, or null. The row is found by its hash and the hashes are then
    /// compared in constant time, so neither the lookup's answer nor its timing says more than "valid" or "not".
    /// </summary>
    public static async Task<AuthToken?> FindValidAsync(AppDbContext db, string? token, string purpose, CancellationToken ct)
    {
        var candidate = token?.Trim() ?? "";
        if (!TokenRegex().IsMatch(candidate))
        {
            return null;
        }

        var hash = Hash(candidate);
        var row = await db.AuthTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (row is null
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(row.TokenHash), Encoding.ASCII.GetBytes(hash))
            || row.Purpose != purpose
            || row.UsedAt is not null
            || row.ExpiresAt <= DateTime.UtcNow)
        {
            return null;
        }

        return row;
    }

    /// <summary>
    /// Trims, checks the shape (MailAddress must parse it back to exactly the same string, so display names and comments
    /// are out) and lower-cases the address for storage and uniqueness. False for anything that is not one address.
    /// </summary>
    public static bool TryNormalizeEmail(string? raw, out string email)
    {
        email = "";
        var trimmed = raw?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Length > MaxEmailLength || !EmailRegex().IsMatch(trimmed))
        {
            return false;
        }

        if (!MailAddress.TryCreate(trimmed, out var parsed) || parsed.Address != trimmed)
        {
            return false;
        }

        email = trimmed.ToLowerInvariant();
        return true;
    }

    /// <summary>
    /// The origin links are built on: Email:PublicOrigin when set, else the request's own origin ONLY when its host is a
    /// loopback name (a laptop, the test host). Any other host with no configured origin gets no link at all: a mailed
    /// link must never be built from a Host header a stranger chose, since the token rides in that link.
    /// </summary>
    public static bool TryOrigin(HttpRequest request, EmailOptions options, out string origin)
    {
        var configured = options.PublicOrigin.Trim().TrimEnd('/');
        if (configured.Length > 0)
        {
            origin = configured;
            return true;
        }

        var host = request.Host.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host == "127.0.0.1" || host == "::1" || host == "[::1]")
        {
            origin = $"{request.Scheme}://{request.Host}";
            return true;
        }

        origin = "";
        return false;
    }

    /// <summary>True when the account already had this many links of the purpose issued inside the window: the brake on mailing one inbox.</summary>
    public static async Task<bool> IssuedAtLeastAsync(AppDbContext db, Guid userId, string purpose, TimeSpan window, int count, CancellationToken ct)
    {
        var since = DateTime.UtcNow - window;
        return await db.AuthTokens.CountAsync(t => t.UserId == userId && t.Purpose == purpose && t.CreatedAt >= since, ct) >= count;
    }

    /// <summary>Three verification links per account every ten minutes and ten a day; three reset links an hour.</summary>
    public static async Task<bool> ThrottledAsync(AppDbContext db, Guid userId, string purpose, CancellationToken ct) =>
        purpose == AuthTokenPurpose.Reset
            ? await IssuedAtLeastAsync(db, userId, purpose, TimeSpan.FromHours(1), 3, ct)
            : await IssuedAtLeastAsync(db, userId, purpose, TimeSpan.FromMinutes(10), 3, ct)
              || await IssuedAtLeastAsync(db, userId, purpose, TimeSpan.FromDays(1), 10, ct);

    public static string VerifyLink(string origin, string token) => $"{origin}/#/verify/{token}";

    public static string ResetLink(string origin, string token) => $"{origin}/#/reset/{token}";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
