namespace FitCheck.Api.Domain;

/// <summary>
/// Round 20: a Stripe webhook event this server already handled, by the event's own id, so a replayed delivery (Stripe
/// retries, a hand-made replay) does nothing twice. Rows older than a month are pruned by the renewal recap's hourly run;
/// the monotonic rules in the webhook stay as the second line of defence for anything older than that.
/// </summary>
public sealed class StripeEvent
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
}
