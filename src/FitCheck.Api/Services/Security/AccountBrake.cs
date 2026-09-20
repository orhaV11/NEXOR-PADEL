using System.Collections.Concurrent;

namespace FitCheck.Api.Services.Security;

/// <summary>
/// A fixed-window counter per string key, in memory, for the brakes that are per account rather than per client address
/// (Round 13): failed sign-ins on one handle, whatever addresses they come from. The address limiters in Program.cs cannot
/// see the handle (it is in the body), so the handler asks here before it checks a password and reports a failure after.
/// Like the rate limiters' windows and the in-flight reservations, it lives in this one process (the README's "single
/// process" limitation); a restart forgets it, which is what a fixed window does at its edge anyway.
/// </summary>
public sealed class AccountBrake
{
    private readonly ConcurrentDictionary<string, (long Window, int Count)> _hits = new();
    private long _lastPrune;

    /// <summary>True when the key already has <paramref name="limit"/> hits or more in the current window.</summary>
    public bool IsBraked(string key, int limit, TimeSpan window, DateTime now)
    {
        var slot = Slot(now, window);
        return _hits.TryGetValue(key, out var hit) && hit.Window == slot && hit.Count >= limit;
    }

    /// <summary>Counts one hit against the key in the current window and returns the count so far.</summary>
    public int Hit(string key, TimeSpan window, DateTime now)
    {
        var slot = Slot(now, window);
        var updated = _hits.AddOrUpdate(key, _ => (slot, 1), (_, hit) => hit.Window == slot ? (slot, hit.Count + 1) : (slot, 1));
        Prune(slot, window, now);
        return updated.Count;
    }

    /// <summary>Forgets the key: a test's reset, or a place where a success should clear the count.</summary>
    public void Clear(string key) => _hits.TryRemove(key, out _);

    /// <summary>Whole seconds until the current fixed window turns over, at least 1: what Retry-After says.</summary>
    public static int SecondsUntilWindowEnds(TimeSpan window, DateTime now)
    {
        var next = (Slot(now, window) + 1) * window.Ticks;
        return Math.Max(1, (int)Math.Ceiling(TimeSpan.FromTicks(next - now.Ticks).TotalSeconds));
    }

    private static long Slot(DateTime now, TimeSpan window) => now.Ticks / window.Ticks;

    /// <summary>Stale windows are dropped now and then, so a script cycling handles cannot grow the table without bound.</summary>
    private void Prune(long slot, TimeSpan window, DateTime now)
    {
        if (_hits.Count < 10_000 || now.Ticks - Interlocked.Read(ref _lastPrune) < window.Ticks)
        {
            return;
        }

        Interlocked.Exchange(ref _lastPrune, now.Ticks);
        foreach (var (key, hit) in _hits)
        {
            if (hit.Window != slot)
            {
                _hits.TryRemove(key, out _);
            }
        }
    }
}
