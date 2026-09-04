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
