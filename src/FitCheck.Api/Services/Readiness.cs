using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>
/// What <c>GET /readyz</c> asks before it says this instance may take traffic: the database answers and is at the current
/// schema, the photo folder takes a file, and ffmpeg is there when clips are meant to be re-encoded. <c>/healthz</c> stays
/// the cheap liveness line the proxy and the container's HEALTHCHECK poll; readiness is what a deploy waits on, and what
/// takes an instance out of rotation when its volume goes away under it.
/// <para>
/// The answer is public (<see cref="ReadyDto"/>: a name and "ok" or a short reason), so nothing here may name a path, a
/// version, a host or a setting's value: "not writable" is the whole truth a stranger gets, and the log line next to it
/// carries the same words for the operator, who knows the paths already.
/// </para>
/// <para>
/// One instance per app (<see cref="HealthEndpoints.MapHealthEndpoints"/> holds it), so it can remember the last answer
/// and log only when it flips: a probe every few seconds must not write a line every few seconds. Flipping to not ready
/// logs at Warning with the failing checks, flipping back logs at Information; a first check that is ready says nothing.
/// </para>
/// </summary>
public sealed class Readiness
{
    /// <summary>The database answers <c>SELECT 1</c> and has no migration left to apply.</summary>
    public const string DatabaseCheck = "db";

    /// <summary>Storage:Root takes a file and gives it back up.</summary>
    public const string StorageCheck = "storage";

    /// <summary>Only while Storage:Transcode is on: the binary was found at start (<see cref="Transcoder.Available"/>).</summary>
    public const string FfmpegCheck = "ffmpeg";

    public const string Ok = "ok";

    private const string Unknown = "unknown";

    /// <summary>1 ready, 0 not ready, -1 nothing asked yet. Read and written by concurrent probes, hence Interlocked.</summary>
    private int _last = -1;

    /// <summary>
    /// Runs every check and remembers the verdict. Never throws: a check that blows up is that check's reason, because a
    /// readiness probe that 500s tells the load balancer nothing it can act on.
    /// </summary>
    public async Task<ReadyDto> CheckAsync(
        AppDbContext db, StorageOptions storage, string contentRoot, Transcoder transcoder, ILogger logger, CancellationToken ct)
    {
        var checks = new Dictionary<string, string>
        {
            [DatabaseCheck] = await DatabaseAsync(db, ct),
            [StorageCheck] = Storage(storage, contentRoot)
        };
        if (storage.Transcode)
        {
            checks[FfmpegCheck] = transcoder.Available ? Ok : "not found";
        }

        var ok = checks.Values.All(reason => reason == Ok);
        Remember(ok, checks, logger);
        return new ReadyDto(ok, checks);
    }

    /// <summary>The database answers, and every migration in the build is recorded as applied (a half-deployed instance is not ready).</summary>
    private static async Task<string> DatabaseAsync(AppDbContext db, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
        }
        catch (Exception)
        {
            return "not answering";
        }

        try
        {
            // Reads the history table; it applies nothing, whatever it finds.
            var pending = await db.Database.GetPendingMigrationsAsync(ct);
            return pending.Any() ? "migrations pending" : Ok;
        }
        catch (Exception)
        {
            return Unknown;
        }
    }

    /// <summary>
    /// A file written into Storage:Root and removed again: the volume is mounted, it is ours to write, and it is not full.
    /// A missing folder, a folder that is really a file and a full disk are all "not writable" to the caller; the
    /// difference is the operator's, from the box.
    /// </summary>
    private static string Storage(StorageOptions storage, string contentRoot)
    {
        var root = Path.IsPathRooted(storage.Root) ? storage.Root : Path.Combine(contentRoot, storage.Root);
        var probe = Path.Combine(root, $".readyz-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return Ok;
        }
        catch (Exception)
        {
            try
            {
                File.Delete(probe);
            }
            catch (Exception)
            {
                // The probe is best-effort on the way out too: the reason below is what matters.
            }

            return "not writable";
        }
    }

    /// <summary>One line when the verdict changes, none while it stays the same.</summary>
    private void Remember(bool ok, Dictionary<string, string> checks, ILogger logger)
    {
        var now = ok ? 1 : 0;
        var before = Interlocked.Exchange(ref _last, now);
        if (before == now || (before < 0 && ok))
        {
            return;
        }

        if (ok)
        {
            logger.LogInformation("Readiness is ok again: every check passes.");
        }
        else
        {
            logger.LogWarning("Readiness failed: {Checks}.",
                string.Join(", ", checks.Where(c => c.Value != Ok).Select(c => $"{c.Key}={c.Value}")));
        }
    }
}
