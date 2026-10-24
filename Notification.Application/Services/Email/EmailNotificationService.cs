namespace Notification.Application.Services.Email;

using System.Threading;
using System.Threading.Tasks;
using Notification.Application.Interfaces;

public sealed class EmailNotificationService : IEmailNotificationService
{
    private readonly IEmailSenderService _emailSender;

    public EmailNotificationService(IEmailSenderService emailSender)
    {
        _emailSender = emailSender;
    }

    public Task<bool> SendOtpEmailAsync(SendOtpEmailDto dto, CancellationToken cancellationToken = default)
    {
        return _emailSender.SendOtpRegistrationEmailAsync(dto.Email, dto.FullName, dto.OtpCode, dto.ExpiresInMinutes, cancellationToken);
    }

    public Task<bool> SendEmailAsync(SendGeneralEmailDto dto, CancellationToken cancellationToken = default)
    {
        return _emailSender.SendEmailAsync(dto.ToEmail, dto.Subject, dto.HtmlBody, cancellationToken);
    }
}
