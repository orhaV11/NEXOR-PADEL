namespace FitCheck.Api.Services;

/// <summary>
/// The clock the weekly board reads (Round 10): the week's window, "closes in", the closer's schedule. One seam so a test
/// can put "now" on the boundary hour in Asia/Jerusalem, across a DST change, or a week later, without waiting. Request
/// handlers elsewhere still read DateTime.UtcNow directly; the board's window math must go through this.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
