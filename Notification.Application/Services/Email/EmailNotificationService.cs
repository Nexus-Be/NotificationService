namespace Notification.Application.Services.Email;

using System.Threading;
using System.Threading.Tasks;
using Notification.Application.Interfaces;

public sealed class EmailNotificationService : IEmailNotificationService
{
    private readonly IEmailSenderService _emailSender;
    private readonly IEmailBackgroundQueue? _backgroundQueue;

    public EmailNotificationService(
        IEmailSenderService emailSender,
        IEmailBackgroundQueue? backgroundQueue = null)
    {
        _emailSender = emailSender;
        _backgroundQueue = backgroundQueue;
    }

    public async Task<bool> SendOtpEmailAsync(SendOtpEmailDto dto, CancellationToken cancellationToken = default)
    {
        if (_backgroundQueue != null)
        {
            await _backgroundQueue.QueueOtpEmailAsync(dto, cancellationToken);
            return true;
        }

        return await _emailSender.SendOtpEmailAsync(dto, cancellationToken);
    }

    public Task<bool> SendEmailAsync(SendGeneralEmailDto dto, CancellationToken cancellationToken = default)
    {
        return _emailSender.SendEmailAsync(dto.ToEmail, dto.Subject, dto.HtmlBody, cancellationToken);
    }
}
