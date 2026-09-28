namespace FitCheck.Api.Domain;

/// <summary>
/// Round 20: one morning push, one row — the receipt that a person was told today's outfit is a tap away, and whether
/// they opened it. The unique key (account, local day) is what makes it once a morning whatever the timer does; this is
/// a receipt, not a spend, which is why a once-a-period key is right here where Round 19 kept one off the suggestions.
/// </summary>
public sealed class TomorrowPush
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateOnly Day { get; set; }
    public DateTime SentAt { get; set; }
    public DateTime? OpenedAt { get; set; }
}
