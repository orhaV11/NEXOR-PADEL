using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>
/// Re-encodes stored clips to H.264 MP4 in the background, so a WebM from an Android phone (or an HEVC MOV out of an iPhone's
/// library) plays on every phone. A check with a clip is <see cref="Enqueue"/>d once it is saved; the one worker copies the
/// clip out of the store, asks ffprobe what it is, runs ffmpeg unless it already is H.264 in an MP4, swaps the file through
/// the store and only then points the check at the new path. A failure keeps the original, which serves as before. Nothing
/// but the file extension records the state, so every start re-queues the clips that are not MP4 yet. Off without ffmpeg
/// (Storage:FfmpegPath, else PATH) or with Storage:Transcode false, when <see cref="Enqueue"/> is a no-op.
/// A guest's clip waits until the check is claimed (nothing plays it before: a guest cannot post), and the claim queues it;
/// so the worker and the claim never move the same file at the same time.
/// </summary>
public class Transcoder : BackgroundService
{
    /// <summary>Jobs waiting for the one worker. Beyond this a clip waits for the sweep at the next start.</summary>
    public const int QueueCapacity = 256;

    /// <summary>How many not-yet-MP4 clips one start re-queues, oldest first.</summary>
    public const int SweepLimit = 200;

    /// <summary>One ffmpeg run may take this long before it is killed; a 30-second phone clip takes seconds on a small VPS.</summary>
    public static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    private readonly Channel<Guid> _queue = Channel.CreateBounded<Guid>(new BoundedChannelOptions(QueueCapacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropWrite
    });

    private readonly IServiceScopeFactory _scopes;
    private readonly IImageStore _store;
    private readonly StorageOptions _options;
    private readonly ILogger<Transcoder> _logger;
    private readonly string? _ffmpeg;
    private readonly string? _ffprobe;

    public Transcoder(IServiceScopeFactory scopes, IImageStore store, IOptions<StorageOptions> options, ILogger<Transcoder> logger)
    {
        _scopes = scopes;
        _store = store;
        _options = options.Value;
        _logger = logger;

        if (!_options.Transcode)
        {
            _logger.LogInformation("Storage:Transcode is off: clips are served as uploaded");
            return;
        }

        // Probed once, here: a missing binary is a deployment fact, not something to discover on every clip.
        var (ffmpeg, ffprobe) = Binaries(_options.FfmpegPath);
        var version = Version(ffmpeg);
        if (version is null)
        {
            _logger.LogInformation("ffmpeg not found ({Path}): clips are served as uploaded. Install ffmpeg or set Storage:FfmpegPath", ffmpeg);
            return;
        }

        if (Version(ffprobe) is null)
        {
            _logger.LogInformation("ffprobe not found next to ffmpeg ({Path}): clips are served as uploaded", ffprobe);
            return;
        }

        _ffmpeg = ffmpeg;
        _ffprobe = ffprobe;
        _logger.LogInformation("Transcoding is on: {Version} at {Path}; clips are re-encoded to H.264 MP4 in the background", version, ffmpeg);
    }

    /// <summary>True when Storage:Transcode is on and ffmpeg (with ffprobe) was found.</summary>
    public virtual bool Available => _ffmpeg is not null;

    /// <summary>Queues one stored clip. Never blocks, never throws; a no-op when unavailable.</summary>
    public virtual void Enqueue(Guid checkId)
    {
        if (!Available)
        {
            return;
        }

        if (!_queue.Writer.TryWrite(checkId))
        {
            _logger.LogInformation("Check {CheckId}: the transcoding queue is full; the clip is picked up at the next start", checkId);
        }
    }

    /// <summary>
    /// Queues every stored clip that is not an MP4 yet (a WebM from before transcoding existed, or one whose run failed),
    /// oldest first and at most <see cref="SweepLimit"/>. Runs once at start; returns how many were queued.
    /// </summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        if (!Available)
        {
            return 0;
        }

        List<Guid> pending;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            pending = await db.Checks
                .Where(c => c.UserId != null && c.VideoPath != null && c.VideoPath != "" && !EF.Functions.Like(c.VideoPath, "%.mp4"))
                .OrderBy(c => c.CreatedAt)
                .Select(c => c.Id)
                .Take(SweepLimit)
                .ToListAsync(ct);
        }

        var queued = 0;
        foreach (var id in pending)
        {
            if (_queue.Writer.TryWrite(id))
            {
                queued++;
            }
        }

        if (pending.Count > 0)
        {
            _logger.LogInformation("Transcoding sweep: {Count} stored clip(s) are not MP4 yet; queued {Queued}", pending.Count, queued);
        }

        return queued;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Available)
        {
            return;
        }

        try
        {
            await SweepAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "The transcoding sweep at start failed; new clips are still transcoded");
        }

        // One reader, one job at a time: never two ffmpeg processes on a small box.
        await foreach (var checkId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(checkId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Check {CheckId}: transcoding failed; the original clip stays", checkId);
            }
        }
    }

    private async Task ProcessAsync(Guid checkId, CancellationToken ct)
    {
        string? videoPath;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Checks.Where(c => c.Id == checkId).Select(c => new { c.UserId, c.VideoPath }).FirstOrDefaultAsync(ct);
            if (row is { UserId: null })
            {
                // A guest's clip: left where it is until the check is claimed, when the claim queues it again. Moving the file
                // now could cross the claim's own copy of it and leave an owned row pointing at the guest folder.
                _logger.LogDebug("Check {CheckId}: a guest's clip; transcoded once it is claimed", checkId);
                return;
            }

            videoPath = row?.VideoPath;
        }

        if (string.IsNullOrEmpty(videoPath))
        {
            // A photo check, or a look deleted before its turn came.
            return;
        }

        // The clip is copied out of the store into a scratch folder: ffmpeg wants files, and the store's layout stays its own.
        var workDir = Path.Combine(Path.GetTempPath(), "orevosh-transcode", checkId.ToString("N"));
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(workDir);
            var input = Path.Combine(workDir, "in" + Path.GetExtension(videoPath));
            long inputBytes;
            await using (var source = _store.OpenRead(videoPath))
            {
                if (source is null)
                {
                    _logger.LogInformation("Check {CheckId}: the clip {Path} is not in the store; nothing to transcode", checkId, videoPath);
                    return;
                }

                await using var copy = new FileStream(input, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
                await source.CopyToAsync(copy, ct);
                inputBytes = copy.Length;
            }

            var codec = await CodecAsync(input, "v:0", ct);
            if (codec == "h264" && videoPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Check {CheckId}: the clip is already H.264 MP4; left as uploaded", checkId);
                return;
            }

            var hasAudio = !string.IsNullOrEmpty(await CodecAsync(input, "a:0", ct));
            var output = Path.Combine(workDir, "out.mp4");
            var arguments = new List<string>
            {
                "-y", "-nostdin", "-hide_banner", "-loglevel", "error",
                "-i", input,
                "-t", _options.MaxVideoSeconds.ToString(CultureInfo.InvariantCulture),
                // At most 1080 wide, both sides even (yuv420p needs that), the aspect kept; phone rotation is applied first.
                "-vf", "scale='trunc(min(1080,iw)/2)*2':-2",
                "-c:v", "libx264", "-preset", "veryfast", "-crf", "26", "-pix_fmt", "yuv420p",
                "-movflags", "+faststart"
            };
            if (hasAudio)
            {
                arguments.AddRange(["-c:a", "aac", "-b:a", "96k", "-ac", "2"]);
            }
            else
            {
                arguments.Add("-an");
            }

            arguments.Add(output);

            var run = await RunAsync(_ffmpeg!, arguments, JobTimeout, ct);
            if (run.TimedOut)
            {
                _logger.LogWarning("Check {CheckId}: ffmpeg was killed after {Seconds} s; the original clip stays", checkId, (int)JobTimeout.TotalSeconds);
                return;
            }

            if (run.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
            {
                _logger.LogWarning("Check {CheckId}: ffmpeg exited with {ExitCode} ({Reason}); the original clip stays", checkId, run.ExitCode, Tail(run.Stderr));
                return;
            }

            long outputBytes;
            string newPath;
            await using (var mp4 = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true))
            {
                outputBytes = mp4.Length;
                try
                {
                    newPath = await _store.ReplaceVideoAsync(videoPath, mp4, ct);
                }
                catch (FileNotFoundException)
                {
                    _logger.LogInformation("Check {CheckId}: the clip went away while it was being transcoded; nothing replaced", checkId);
                    return;
                }
            }

            // The row moves only while it still points at the clip this run started from. A look deleted meanwhile has already
            // dropped its clip, and the MP4 must go the same way rather than sit on disk without a row.
            int updated;
            using (var scope = _scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                updated = await db.Checks
                    .Where(c => c.Id == checkId && c.VideoPath == videoPath)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.VideoPath, newPath), ct);
            }

            if (updated == 0)
            {
                _store.Delete(newPath);
                _logger.LogInformation("Check {CheckId}: the look went away while its clip was being transcoded; the MP4 was discarded", checkId);
                return;
            }

            _logger.LogInformation(
                "Check {CheckId}: clip transcoded {Codec} {From} -> h264 {To}, {InputBytes} -> {OutputBytes} bytes in {Ms} ms",
                checkId, codec ?? "unknown", Path.GetExtension(videoPath), Path.GetExtension(newPath), inputBytes, outputBytes, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Scratch only; the next run of this check reuses the folder.
            }
        }
    }

    /// <summary>The codec name of the first stream of a kind ("v:0", "a:0"), or null when there is none or ffprobe cannot read the file.</summary>
    private async Task<string?> CodecAsync(string file, string stream, CancellationToken ct)
    {
        var result = await RunAsync(_ffprobe!, ["-v", "error", "-select_streams", stream, "-show_entries", "stream=codec_name", "-of", "csv=p=0", file], ProbeTimeout, ct);
        if (result.TimedOut || result.ExitCode != 0)
        {
            return null;
        }

        var line = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrEmpty(line) ? null : line.TrimEnd(',');
    }

    /// <summary>ffmpeg from Storage:FfmpegPath or PATH, and the ffprobe that ships next to it.</summary>
    private static (string Ffmpeg, string Ffprobe) Binaries(string configured)
    {
        var path = configured.Trim();
        if (path.Length == 0)
        {
            return ("ffmpeg", "ffprobe");
        }

        var directory = Path.GetDirectoryName(path) ?? "";
        return (path, Path.Combine(directory, "ffprobe" + Path.GetExtension(path)));
    }

    /// <summary>The first line of <c>-version</c> up to the copyright, or null when the binary is not there or does not answer.</summary>
    private static string? Version(string fileName)
    {
        try
        {
            var result = RunAsync(fileName, ["-version"], ProbeTimeout, CancellationToken.None).GetAwaiter().GetResult();
            if (result.TimedOut || result.ExitCode != 0)
            {
                return null;
            }

            var line = result.Stdout.Split('\n', 2)[0].Trim();
            var copyright = line.IndexOf(" Copyright", StringComparison.OrdinalIgnoreCase);
            return line.Length == 0 ? null : copyright > 0 ? line[..copyright] : line;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

    /// <summary>Runs one process with the arguments passed as they are (no shell), both pipes drained, killed at the timeout.</summary>
    private static async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = info };
        process.Start();
        // Read while it runs: a full stderr pipe would otherwise block ffmpeg on its own error output.
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            return new ProcessResult(-1, await stdout, await stderr, TimedOut: true);
        }

        return new ProcessResult(process.ExitCode, await stdout, await stderr, TimedOut: false);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone.
        }
    }

    /// <summary>The last line ffmpeg wrote, for one log line without the whole transcript.</summary>
    private static string Tail(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var last = lines.Length == 0 ? "no output" : lines[^1];
        return last.Length > 200 ? last[..200] : last;
    }
}
