namespace Notification.Infrastructure.Services.Email;

using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Notification.Application.Interfaces;
using Notification.Application.Services.Deliveries;

/// <summary>Strict SMTP adapter for durable deliveries; never simulates a successful send.</summary>
public sealed class SmtpEmailDeliverySender : IEmailDeliverySender
{
    private readonly IOptionsMonitor<SmtpOptions> _options;
    private readonly ILogger<SmtpEmailDeliverySender> _logger;
    public SmtpEmailDeliverySender(IOptionsMonitor<SmtpOptions> options, ILogger<SmtpEmailDeliverySender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<EmailDeliveryResult> SendAsync(EmailDeliveryMessage request, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password) || string.IsNullOrWhiteSpace(options.Host))
            return EmailDeliveryResult.Failure("SMTP_NOT_CONFIGURED", "SMTP credentials or host are not configured.");
        try
        {
            var message = new MimeMessage { Subject = request.Subject, MessageId = request.MessageId };
            message.From.Add(new MailboxAddress(options.SenderName, string.IsNullOrWhiteSpace(options.SenderEmail) ? options.Username : options.SenderEmail));
            message.To.Add(MailboxAddress.Parse(request.Recipient));
            message.Body = new BodyBuilder { HtmlBody = request.HtmlBody }.ToMessageBody();
            using var client = new SmtpClient();
            await client.ConnectAsync(options.Host, options.Port, options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);
            await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            // Close locally after DATA acceptance; do not wait on QUIT or cancellation after a successful send.
            try { await client.DisconnectAsync(false, CancellationToken.None); }
            catch (Exception ex) { _logger.LogWarning(ex, "SMTP disconnect failed after accepting delivery {DeliveryId}.", request.DeliveryId); }
            return EmailDeliveryResult.Sent();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTP attempt failed for delivery {DeliveryId}.", request.DeliveryId);
            return ClassifyFailure(ex);
        }
    }

    public static EmailDeliveryResult ClassifyFailure(Exception exception) => exception switch
    {
        SmtpCommandException smtp => EmailDeliveryResult.Failure($"SMTP_{(int)smtp.StatusCode}",
            "SMTP server rejected the request.", (int)smtp.StatusCode is >= 400 and < 500),
        AuthenticationException or ServiceNotAuthenticatedException or System.Security.Authentication.AuthenticationException =>
            EmailDeliveryResult.Failure("SMTP_AUTH_FAILED", "SMTP authentication failed."),
        FormatException or ArgumentException => EmailDeliveryResult.Failure("EMAIL_INVALID", "Email message or address is invalid."),
        IOException or SocketException or TimeoutException or SmtpProtocolException or OperationCanceledException =>
            EmailDeliveryResult.Failure("SMTP_TEMPORARY_ERROR", "SMTP connection failed temporarily.", true),
        _ => EmailDeliveryResult.Failure("SMTP_UNEXPECTED_ERROR", "SMTP request failed unexpectedly.", true)
    };
}
