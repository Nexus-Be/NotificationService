namespace Notification.Infrastructure.Services.Email;

using System;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Notification.Application.Interfaces;

public sealed class MailKitEmailSenderService : IEmailService, IEmailSenderService, Notification.Application.Common.Interfaces.IEmailSenderService
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

        if (string.IsNullOrWhiteSpace(opts.Username)
            || string.IsNullOrWhiteSpace(opts.Password))
        {
            _logger.LogError(
                "Email was not sent because SMTP credentials are missing."
            );
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(opts.SenderName, string.IsNullOrWhiteSpace(opts.SenderEmail) ? opts.Username : opts.SenderEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            var secureSocketOptions = opts.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(opts.Host, opts.Port, secureSocketOptions, cancellationToken);
            await client.AuthenticateAsync(opts.Username, opts.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Successfully sent email to '{ToEmail}' via SMTP ({Host}:{Port}).", toEmail, opts.Host, opts.Port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to '{ToEmail}' via SMTP ({Host}:{Port}).", toEmail, opts.Host, opts.Port);
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
        var subject = "Nexus - Your Verification Code (OTP)";
        var displayName = string.IsNullOrWhiteSpace(fullName) ? "Customer" : fullName.Trim();

        var htmlBody = $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="utf-8">
                <style>
                    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f9fafb; margin: 0; padding: 20px; }
                    .container { max-width: 560px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; border: 1px solid #e5e7eb; padding: 32px; }
                    .header { text-align: center; border-bottom: 1px solid #f3f4f6; padding-bottom: 20px; margin-bottom: 24px; }
                    .title { font-size: 22px; font-weight: 700; color: #111827; margin: 0; }
                    .content { color: #374151; font-size: 15px; line-height: 1.6; }
                    .otp-box { background-color: #f3f4f6; border-radius: 6px; padding: 18px; text-align: center; margin: 28px 0; font-size: 32px; font-weight: 800; letter-spacing: 6px; color: #1f2937; }
                    .footer { font-size: 13px; color: #9ca3af; text-align: center; margin-top: 32px; border-top: 1px solid #f3f4f6; padding-top: 20px; }
                </style>
            </head>
            <body>
                <div class="container">
                    <div class="header">
                        <h1 class="title">Nexus Ecosystem</h1>
                    </div>
                    <div class="content">
                        <p>Hello <strong>{{displayName}}</strong>,</p>
                        <p>Thank you for registering an account on the Nexus Platform. Please use the verification code below to activate your account:</p>
                        <div class="otp-box">{{otp}}</div>
                        <p>This code will expire in <strong>{{expiresInMinutes}} minutes</strong>. For security reasons, please do not share this code with anyone.</p>
                        <p>If you did not request this registration, please ignore this email.</p>
                    </div>
                    <div class="footer">
                        <p>&copy; {{DateTime.UtcNow.Year}} Nexus E-Commerce & Auction Ecosystem. All rights reserved.</p>
                    </div>
                </div>
            </body>
            </html>
            """;

        return await SendEmailAsync(toEmail, subject, htmlBody, cancellationToken);
    }
}
