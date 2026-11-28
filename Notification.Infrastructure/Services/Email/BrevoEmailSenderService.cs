namespace Notification.Infrastructure.Services.Email;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Notification.Application.Interfaces;
using Notification.Application.Services.Deliveries;
using Notification.Application.Services.Email;

public sealed class BrevoEmailSenderService : IEmailService, IEmailSenderService
{
    private readonly IEmailDeliverySender _deliverySender;
    private readonly ILogger<BrevoEmailSenderService> _logger;

    public BrevoEmailSenderService(
        IEmailDeliverySender deliverySender,
        ILogger<BrevoEmailSenderService> logger)
    {
        _deliverySender = deliverySender;
        _logger = logger;
    }

    public async Task<bool> SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        var message = new EmailDeliveryMessage(Guid.NewGuid(), toEmail, subject, htmlBody);
        var result = await _deliverySender.SendAsync(message, cancellationToken);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Failed to send email to '{ToEmail}' via Brevo ({ErrorCode}): {ErrorMessage}",
                toEmail,
                result.ErrorCode,
                result.ErrorMessage);
            return false;
        }

        _logger.LogInformation("Successfully sent email to '{ToEmail}' via Brevo API.", toEmail);
        return true;
    }

    public Task<bool> SendOtpRegistrationEmailAsync(
        string toEmail,
        string fullName,
        string otp,
        int expiresInMinutes = 5,
        CancellationToken cancellationToken = default)
    {
        return SendOtpEmailAsync(toEmail, fullName, otp, expiresInMinutes, "registration", cancellationToken);
    }

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

    public Task<bool> SendOtpEmailAsync(
        SendOtpEmailDto request,
        CancellationToken cancellationToken = default)
    {
        return SendOtpEmailAsync(
            request.Email,
            request.FullName,
            request.OtpCode,
            request.ExpiresInMinutes,
            request.Purpose,
            cancellationToken);
    }
}
