namespace FitCheck.Api.Services;

public enum CapacityVerdict
{
    Ok,
    UserCapReached,
    GlobalCapReached
}

/// <summary>
/// Counts checks that are in flight but not yet stored, so parallel uploads cannot all slip under the cap
/// between the database count and the model call. In-process only, which matches the single-process pilot.
/// </summary>
public sealed class CheckCapacity
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, int> _perUser = [];
    private int _total;

    public CapacityVerdict TryReserve(Guid userId, int storedForUser, int userCap, int storedGlobal, int globalCap, out IDisposable? reservation)
    {
        reservation = null;
        lock (_lock)
        {
            var inFlightForUser = _perUser.GetValueOrDefault(userId);
            if (storedForUser + inFlightForUser >= userCap)
            {
                return CapacityVerdict.UserCapReached;
            }

            if (storedGlobal + _total >= globalCap)
            {
                return CapacityVerdict.GlobalCapReached;
            }

            _perUser[userId] = inFlightForUser + 1;
            _total++;
        }

        reservation = new Reservation(this, userId);
        return CapacityVerdict.Ok;
    }

    private void Release(Guid userId)
    {
        lock (_lock)
        {
            var remaining = _perUser.GetValueOrDefault(userId) - 1;
            if (remaining <= 0)
            {
                _perUser.Remove(userId);
            }
            else
            {
                _perUser[userId] = remaining;
            }

            _total = Math.Max(0, _total - 1);
        }
    }

    private sealed class Reservation(CheckCapacity owner, Guid userId) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (!_released)
            {
                _released = true;
                owner.Release(userId);
            }
        }
    }
}

/// <summary>
/// The per-address half of the guest cap: Plans:GuestChecksPerDay checks per client address over a rolling 24 hours,
/// counted in memory from what this process stored. A check counts once its row is saved with a status other than error
/// (ok, not_outfit and rejected all cost a model call), so a refused upload, a model outage or a dropped connection never
/// spends an address's look, exactly as the per-cookie count in the database works. Checks in flight are reserved the way
/// <see cref="CheckCapacity"/> reserves them, so a burst of cookieless requests from one router cannot all slip under the
/// count. In-process only, which matches the single-process pilot (a restart forgets the day); the "guest" rate-limit policy
/// (Plans:GuestAttemptsPerDay) stays in front of it as the brake on attempts.
/// </summary>
public sealed class GuestAddressCounter
{
    private const int PruneEvery = 256;

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _addresses = new(StringComparer.Ordinal);
    private int _sincePrune;

    /// <summary>
    /// Reserves one check for the address, or refuses when the stored checks plus those in flight reach the cap. On a
    /// refusal <paramref name="retryAfterSeconds"/> says when the next permit frees up, when a stored check can tell.
    /// </summary>
    public bool TryReserve(string address, int cap, DateTime now, out Reservation? reservation, out int? retryAfterSeconds)
    {
        reservation = null;
        retryAfterSeconds = null;
        lock (_lock)
        {
            if (++_sincePrune >= PruneEvery)
            {
                _sincePrune = 0;
                Prune(now);
            }

            _addresses.TryGetValue(address, out var entry);
            entry?.Trim(now);
            var stored = entry?.Stored.Count ?? 0;
            var inFlight = entry?.InFlight ?? 0;
            if (stored + inFlight >= cap)
            {
                retryAfterSeconds = entry is null ? null : Spend.RetryAfterSeconds(entry.Stored, cap, now);
                return false;
            }

            if (entry is null)
            {
                entry = new Entry();
                _addresses[address] = entry;
            }

            entry.InFlight++;
        }

        reservation = new Reservation(this, address);
        return true;
    }

    /// <summary>How many stored checks the address has in the window right now (tests and diagnostics).</summary>
    public int Stored(string address, DateTime now)
    {
        lock (_lock)
        {
            if (!_addresses.TryGetValue(address, out var entry))
            {
                return 0;
            }

            entry.Trim(now);
            return entry.Stored.Count;
        }
    }

    private void Commit(string address, DateTime storedAt)
    {
        lock (_lock)
        {
            if (!_addresses.TryGetValue(address, out var entry))
            {
                entry = new Entry();
                _addresses[address] = entry;
            }
            else
            {
                entry.InFlight = Math.Max(0, entry.InFlight - 1);
            }

            // Oldest first, like the database's list: requests stamp their own start, so a slow one may land behind a faster one.
            var at = entry.Stored.FindIndex(t => t > storedAt);
            entry.Stored.Insert(at < 0 ? entry.Stored.Count : at, storedAt);
        }
    }

    private void Release(string address)
    {
        lock (_lock)
        {
            if (!_addresses.TryGetValue(address, out var entry))
            {
                return;
            }

            entry.InFlight = Math.Max(0, entry.InFlight - 1);
            if (entry.InFlight == 0 && entry.Stored.Count == 0)
            {
                _addresses.Remove(address);
            }
        }
    }

    private void Prune(DateTime now)
    {
        foreach (var (address, entry) in _addresses.ToList())
        {
            entry.Trim(now);
            if (entry.InFlight == 0 && entry.Stored.Count == 0)
            {
                _addresses.Remove(address);
            }
        }
    }

    private sealed class Entry
    {
        public List<DateTime> Stored { get; } = [];
        public int InFlight { get; set; }

        /// <summary>Drops the stamps that left the window (the same "created at or after the window start" the database count uses).</summary>
        public void Trim(DateTime now)
        {
            var windowStart = now - Spend.Window;
            Stored.RemoveAll(t => t < windowStart);
        }
    }

    /// <summary>
    /// One check in flight for an address. <see cref="Commit"/> turns it into a stored check that counts for the day;
    /// disposing without committing (a refusal, a failed call, a dropped connection) only gives the slot back.
    /// </summary>
    public sealed class Reservation(GuestAddressCounter owner, string address) : IDisposable
    {
        private int _settled;

        public void Commit(DateTime storedAt)
        {
            if (Interlocked.Exchange(ref _settled, 1) == 0)
            {
                owner.Commit(address, storedAt);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _settled, 1) == 0)
            {
                owner.Release(address);
            }
        }
    }
}
