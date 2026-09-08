using FitCheck.Api.Domain;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>One outgoing message. Bodies are plain text; the link is the point.</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>Sends verification and reset links. The recovery builder adds the SMTP implementation.</summary>
public interface IEmailSender
{
    /// <summary>False when no mail is configured: the client then says recovery is off on this server.</summary>
    bool Enabled { get; }

    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>Development stand-in: writes the message (link included) to the log instead of sending it.</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger, IOptions<EmailOptions> options) : IEmailSender
{
    public bool Enabled => options.Value.Enabled;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        logger.LogInformation("Email to {To}: {Subject}\n{Body}", message.To, message.Subject, message.Body);
        return Task.CompletedTask;
    }
}
