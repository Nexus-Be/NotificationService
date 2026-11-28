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
using Notification.Application.Services.Email;

public sealed class MailKitEmailSenderService : IEmailService, IEmailSenderService
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

        if (string.IsNullOrWhiteSpace(opts.Username) || string.IsNullOrWhiteSpace(opts.Password) || string.IsNullOrWhiteSpace(opts.Host))
        {
            _logger.LogWarning("SMTP configuration is missing. Email was not sent.");
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
            try { await client.DisconnectAsync(false, CancellationToken.None); }
            catch (Exception ex) { _logger.LogWarning(ex, "SMTP disconnect failed after message acceptance."); }

            _logger.LogInformation("Successfully sent email to '{ToEmail}' via SMTP ({Host}:{Port}).", toEmail, opts.Host, opts.Port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to '{ToEmail}' via SMTP ({Host}:{Port}).", toEmail, opts.Host, opts.Port);
            return false;
        }
    }

    // Giữ lại phương thức cũ để tương thích ngược nếu cần
    public async Task<bool> SendOtpRegistrationEmailAsync(
        string toEmail,
        string fullName,
        string otp,
        int expiresInMinutes = 5,
        CancellationToken cancellationToken = default)
    {
        return await SendOtpEmailAsync(toEmail, fullName, otp, expiresInMinutes, "registration", cancellationToken);
    }


    // Phương thức mới hỗ trợ tùy biến theo purpose (password_reset, seller_registration, registration)
    public async Task<bool> SendOtpEmailAsync(
        string toEmail,
        string fullName,
        string otp,
        int expiresInMinutes,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        var (subject, htmlBody) = OtpEmailContentBuilder.Build(fullName, otp, expiresInMinutes, purpose);
        return await SendEmailAsync(toEmail, subject, htmlBody, cancellationToken);
    }

    public async Task<bool> SendOtpEmailAsync(
        SendOtpEmailDto request,
        CancellationToken cancellationToken = default)
    {
        return await SendOtpEmailAsync(
            request.Email,
            request.FullName,
            request.OtpCode,
            request.ExpiresInMinutes,
            request.Purpose,
            cancellationToken);
    }
}