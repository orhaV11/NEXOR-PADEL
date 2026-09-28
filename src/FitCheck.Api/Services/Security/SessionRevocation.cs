using System.Collections.Concurrent;
using FitCheck.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services.Security;

/// <summary>
/// Server-side sign-out (Round 13). A session is a cookie holding an encrypted ticket, and a ticket is valid until it
/// expires: deleting the cookie on logout signed the browser out but never the ticket, so a copy taken before (a shared
/// laptop, a stolen device) kept working for up to ninety days. Now every ticket carries a random session id
/// (<see cref="Sessions.SignInAsync"/>) and this service is asked on every authenticated request whether that id was
/// revoked (logout: this device only) or whether the ticket was issued before the account's cutoff (a password reset:
/// every other session ends). A ticket without an id (issued before this round) is refused, so the deploy signs everyone
/// in again once and no session without an id survives it.
/// <para>
/// The rows live in the Counters table (a row is any name and a number: no new table, no migration): "revoked_session:&lt;id&gt;"
/// with the ticket's expiry, "session_cutoff:&lt;user&gt;" with the cutoff, both as unix seconds. They are read once into
/// memory (this is one process, README's "single process" limitation) and kept in step from then on; revocations past
/// their ticket's expiry are deleted when a new one is written. A request costs a dictionary lookup, never a query.
/// </para>
/// </summary>
public sealed class SessionRevocation
{
    public const string RevokedPrefix = "revoked_session:";
    public const string CutoffPrefix = "session_cutoff:";

    private readonly ConcurrentDictionary<string, long> _revoked = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, long> _cutoffs = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile bool _loaded;

    /// <summary>The cookie event: refuses a ticket without a session id, a revoked one, and one issued before the account's cutoff.</summary>
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var revocation = context.HttpContext.RequestServices.GetRequiredService<SessionRevocation>();
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        await revocation.EnsureLoadedAsync(db, context.HttpContext.RequestAborted);

        var sessionId = Sessions.SessionId(context.Properties);
        var userId = context.Principal is null ? null : Sessions.UserId(context.Principal);
        var issued = context.Properties.IssuedUtc?.ToUnixTimeSeconds() ?? 0;
        if (sessionId is null || revocation._revoked.ContainsKey(sessionId)
            || (userId is { } id && revocation._cutoffs.TryGetValue(id, out var cutoff) && issued < cutoff))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    /// <summary>Ends one session: its id is refused from now until the ticket would have expired anyway.</summary>
    public async Task RevokeAsync(AppDbContext db, string sessionId, DateTimeOffset? expires, DateTime now, CancellationToken ct)
    {
        await EnsureLoadedAsync(db, ct);
        var nowSeconds = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var until = (expires ?? DateTimeOffset.UtcNow.AddDays(90)).ToUnixTimeSeconds();
        _revoked[sessionId] = until;
        await UpsertAsync(db, RevokedPrefix + sessionId, until, ct);
        // Housekeeping on the way: revocations whose tickets have expired are no longer needed.
        var pattern = RevokedPrefix + "%";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"Counters\" WHERE \"Name\" LIKE {pattern} AND \"Value\" < {nowSeconds}", ct);
        foreach (var (id, expiry) in _revoked)
        {
            if (expiry < nowSeconds)
            {
                _revoked.TryRemove(id, out _);
            }
        }
    }

    /// <summary>Ends every session of the account that was signed in before now (a password reset); the one signed in next passes.</summary>
    public async Task CutOffAsync(AppDbContext db, Guid userId, DateTime now, CancellationToken ct)
    {
        await EnsureLoadedAsync(db, ct);
        var seconds = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToUnixTimeSeconds();
        _cutoffs[userId] = seconds;
        await UpsertAsync(db, CutoffPrefix + userId.ToString("N"), seconds, ct);
    }

    /// <summary>True when the session id is refused; for the tests and the doctor.</summary>
    public bool IsRevoked(string sessionId) => _revoked.ContainsKey(sessionId);

    private async Task EnsureLoadedAsync(AppDbContext db, CancellationToken ct)
    {
        if (_loaded)
        {
            return;
        }

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_loaded)
            {
                return;
            }

            var revokedPattern = RevokedPrefix + "%";
            var cutoffPattern = CutoffPrefix + "%";
            var rows = await db.Counters.AsNoTracking()
                .Where(c => EF.Functions.Like(c.Name, revokedPattern) || EF.Functions.Like(c.Name, cutoffPattern))
                .ToListAsync(ct);
            var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var row in rows)
            {
                if (row.Name.StartsWith(RevokedPrefix, StringComparison.Ordinal))
                {
                    if (row.Value >= nowSeconds)
                    {
                        _revoked[row.Name[RevokedPrefix.Length..]] = row.Value;
                    }
                }
                else if (Guid.TryParseExact(row.Name[CutoffPrefix.Length..], "N", out var userId))
                {
                    _cutoffs[userId] = row.Value;
                }
            }

            _loaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private static Task UpsertAsync(AppDbContext db, string name, long value, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"Counters\" (\"Name\", \"Value\") VALUES ({name}, {value}) ON CONFLICT(\"Name\") DO UPDATE SET \"Value\" = excluded.\"Value\"", ct);
}
