using FitCheck.Api.Domain;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FitCheck.Api.Services;

/// <summary>One outgoing message. Bodies are plain text; the link is the point.</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>Sends verification and reset links: <see cref="SmtpEmailSender"/> when mail is configured, the log otherwise.</summary>
public interface IEmailSender
{
    /// <summary>False when no mail is configured: the client then says recovery is off on this server.</summary>
    bool Enabled { get; }

    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>
/// Development stand-in: writes the message (link included) to the log instead of sending it. Enabled only when the
/// options say mail is on, which for this sender means <c>Email:Host=log</c>: the links are produced and logged, and no
/// server is ever contacted. With Host and From empty it stays off and nothing asks it to send.
/// </summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger, IOptions<EmailOptions> options) : IEmailSender
{
    /// <summary>The host name that means "log the links instead of sending them": for local runs and the browser test.</summary>
    public const string LogHost = "log";

    public static bool IsLogHost(EmailOptions options) => string.Equals(options.Host.Trim(), LogHost, StringComparison.OrdinalIgnoreCase);

    public bool Enabled => options.Value.Enabled;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        logger.LogInformation("Email to {To}: {Subject}\n{Body}", message.To, message.Subject, message.Body);
        return Task.CompletedTask;
    }
}

/// <summary>
/// The real thing, over SMTP with MailKit: host, port, STARTTLS and credentials from <see cref="EmailOptions"/>, plain-text
/// bodies, 15 seconds for the whole exchange. Failures are logged and rethrown; the endpoint decides what the person sees.
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public bool Enabled => options.Value.Enabled;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var settings = options.Value;
        try
        {
            var mime = new MimeMessage();
            mime.From.Add(MailboxAddress.Parse(settings.From));
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            mime.Body = new TextPart("plain") { Text = message.Body };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
            // STARTTLS is the norm on 587; 465 is implicit TLS; anything else is left to MailKit's guess by port.
            var security = settings.UseStartTls ? SecureSocketOptions.StartTls
                : settings.Port == 465 ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.Auto;
            await client.ConnectAsync(settings.Host, settings.Port, security, cts.Token);
            if (!string.IsNullOrEmpty(settings.User))
            {
                await client.AuthenticateAsync(settings.User, settings.Password, cts.Token);
            }

            await client.SendAsync(mime, cts.Token);
            await client.DisconnectAsync(quit: true, cts.Token);
            logger.LogInformation("Email sent to {To}: {Subject}", message.To, message.Subject);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Email to {To} failed: {Subject}", message.To, message.Subject);
            throw;
        }
    }
}
