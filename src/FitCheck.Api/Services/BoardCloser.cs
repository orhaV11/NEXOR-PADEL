using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>
/// Closes the board's weeks (Round 10). Every five minutes it asks the board's clock which weeks are over and writes the
/// <see cref="WeeklyWinner"/> rows of every week that has none yet, oldest first, back to the week of the earliest fire:
/// the close of the week that just ended, and after downtime the weeks that were missed. The top of the looks board is
/// told its place (<see cref="NotificationType.BoardRank"/>, in the app and by push) for the most recent week only; a
/// catch-up over older weeks is silent. A week with no counted fires writes nothing (and says so once). The unique index
/// on (WeekStart, Board, Rank) makes a second close of the same week - a restart, two processes - a no-op: the constraint
/// fails, the rows and the notifications of that save are dropped, and the week stands as the first run wrote it.
/// </summary>
public sealed class BoardCloser(IServiceScopeFactory scopes, Board board, IClock clock, ILogger<BoardCloser> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    // Weeks found to have nothing to close in this process: logged once, not recomputed every five minutes.
    private readonly HashSet<DateOnly> _empty = [];
    private readonly SemaphoreSlim _running = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await CloseSafelyAsync(stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CloseSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task CloseSafelyAsync(CancellationToken ct)
    {
        try
        {
            await CloseDueWeeksAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Board: the close failed; it runs again in five minutes");
        }
    }

    /// <summary>
    /// Closes every week that is over and not yet closed, oldest first. Returns the archive rows written. Safe to call
    /// from a test or on a schedule; two calls at once run one after the other.
    /// </summary>
    public async Task<int> CloseDueWeeksAsync(CancellationToken ct)
    {
        await _running.WaitAsync(ct);
        try
        {
            return await CloseDueWeeksCoreAsync(ct);
        }
        finally
        {
            _running.Release();
        }
    }

    private async Task<int> CloseDueWeeksCoreAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var current = board.WeekOf(now);
        var last = board.Previous(current);

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var earliest = await db.Fires.OrderBy(f => f.CreatedAt).Select(f => (DateTime?)f.CreatedAt).FirstOrDefaultAsync(ct);
        if (earliest is null)
        {
            return 0;
        }

        var written = 0;
        for (var week = board.WeekOf(earliest.Value); week.End <= now; week = board.Next(week))
        {
            if (_empty.Contains(week.FirstDay) || await Board.IsClosedAsync(db, week, ct))
            {
                continue;
            }

            var result = await board.ComputeAsync(db, week, ct);
            if (result.CountedFires == 0)
            {
                _empty.Add(week.FirstDay);
                logger.LogInformation("Board: week {WeekStart} had no counted fires, nothing to close", week.Key);
                continue;
            }

            written += await WriteAsync(scope.ServiceProvider, db, result, notify: week.FirstDay == last.FirstDay, ct);
        }

        return written;
    }

    /// <summary>
    /// The archive rows of one week in one save, with the looks board's notifications when asked (one per person, their
    /// best place). Returns the rows written: 0 when another run closed the week first.
    /// </summary>
    private async Task<int> WriteAsync(IServiceProvider services, AppDbContext db, BoardResult result, bool notify, CancellationToken ct)
    {
        var week = result.Week;
        var label = week.Label;
        var rows = result.All()
            .Select(place => new WeeklyWinner
            {
                Id = Guid.NewGuid(),
                WeekStart = label,
                Board = place.Board,
                Rank = place.Entry.Rank,
                PostId = place.Entry.PostId,
                UserId = place.Entry.UserId,
                Fires = place.Entry.Fires,
                Score = place.Board == BoardName.Picks ? place.Entry.Score : null
            })
            .ToList();
        db.WeeklyWinners.AddRange(rows);

        if (notify)
        {
            var notifier = services.GetRequiredService<Notifier>();
            var winners = result.Looks.GroupBy(e => e.UserId).Select(g => g.OrderBy(e => e.Rank).First()).ToList();
            var winnerIds = winners.Select(w => w.UserId).ToList();
            var handles = await db.Users.Where(u => winnerIds.Contains(u.Id)).Select(u => new { u.Id, u.Handle }).ToDictionaryAsync(u => u.Id, u => u.Handle, ct);
            foreach (var winner in winners)
            {
                if (handles.TryGetValue(winner.UserId, out var handle))
                {
                    // The actor is the person themselves: the line reads the rank, the tap lands on the board.
                    notifier.Add(winner.UserId, NotificationType.BoardRank, handle, winner.PostId, rank: winner.Rank);
                }
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            logger.LogInformation("Board: week {WeekStart} was already closed by another run; nothing written", week.Key);
            return 0;
        }

        logger.LogInformation("Board: week {WeekStart} closed, {Rows} rows", week.Key, rows.Count);
        return rows.Count;
    }

    /// <summary>SQLite's constraint error (19) for the unique index on week, board and rank.</summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: 19 } || ex.InnerException?.InnerException is SqliteException { SqliteErrorCode: 19 };
}
