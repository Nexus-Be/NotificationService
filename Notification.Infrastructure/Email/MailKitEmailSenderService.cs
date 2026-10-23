namespace Notification.Infrastructure.Email;

using System;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Notification.Application.Common.Interfaces;

public sealed class MailKitEmailSenderService : IEmailSenderService
{
    private readonly IOptionsMonitor<SmtpOptions> _smtpOptions;
    private readonly ILogger<MailKitEmailSenderService> _logger;

    public MailKitEmailSenderService(
        IOptionsMonitor<SmtpOptions> smtpOptions,
        ILogger<MailKitEmailSenderService> logger)
    {
        _smtpOptions = smtpOptions;
        _logger = logger;
    }

    public async Task<bool> SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        var opts = _smtpOptions.CurrentValue;

        // Nếu chưa cấu hình thông tin đăng nhập SMTP (Local development simulation)
        if (string.IsNullOrWhiteSpace(opts.Username) || string.IsNullOrWhiteSpace(opts.Password))
        {
            _logger.LogWarning(
                "[EMAIL SIMULATION] SMTP credentials are not configured in appsettings. Simulated sending email to '{ToEmail}' with subject '{Subject}'.",
                toEmail,
                subject);
            return true;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(opts.SenderName, string.IsNullOrWhiteSpace(opts.SenderEmail) ? opts.Username : opts.SenderEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            var secureSocketOptions = opts.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

            await client.ConnectAsync(opts.Host, opts.Port, secureSocketOptions, cancellationToken);
            await client.AuthenticateAsync(opts.Username, opts.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Successfully sent email to '{ToEmail}' with subject '{Subject}'.", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to '{ToEmail}' via SMTP host '{Host}': {ErrorMessage}", toEmail, opts.Host, ex.Message);
            return false;
        }
    }

    public async Task<bool> SendOtpRegistrationEmailAsync(
        string toEmail,
        string fullName,
        string otp,
        int expiresInMinutes = 5,
        CancellationToken cancellationToken = default)
    {
        var subject = $"Nexus Account Verification Code: {otp}";
        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"">
  <style>
    body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f6f9fc; margin: 0; padding: 20px; }}
    .container {{ max-width: 580px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.08); }}
    .header {{ background-color: #1a1a2e; color: #ffffff; padding: 24px 32px; text-align: center; }}
    .header h1 {{ margin: 0; font-size: 22px; font-weight: 600; letter-spacing: 0.5px; }}
    .content {{ padding: 32px; color: #333333; line-height: 1.6; }}
    .otp-box {{ background-color: #f0f4f8; border: 2px dashed #0066cc; border-radius: 8px; padding: 18px; text-align: center; margin: 28px 0; }}
    .otp-code {{ font-size: 32px; font-weight: 700; letter-spacing: 8px; color: #0066cc; }}
    .footer {{ background-color: #fafbfc; border-top: 1px solid #e1e4e8; padding: 16px 32px; font-size: 13px; color: #666666; text-align: center; }}
  </style>
</head>
<body>
  <div class=""container"">
    <div class=""header"">
      <h1>NEXUS ECOSYSTEM</h1>
    </div>
    <div class=""content"">
      <p>Hello <strong>{fullName}</strong>,</p>
      <p>Thank you for signing up with Nexus. To complete your account verification, please enter the verification code below:</p>
      <div class=""otp-box"">
        <div class=""otp-code"">{otp}</div>
      </div>
      <p>This verification code is valid for <strong>{expiresInMinutes} minutes</strong>.</p>
      <p style=""color: #888888; font-size: 13px;"">If you did not initiate this registration request, please disregard this email.</p>
    </div>
    <div class=""footer"">
      &copy; {DateTime.UtcNow.Year} Nexus Microservices Ecosystem. All rights reserved.
    </div>
  </div>
</body>
</html>";

        return await SendEmailAsync(toEmail, subject, htmlBody, cancellationToken);
    }
}
