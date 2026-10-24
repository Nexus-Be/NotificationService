namespace Notification.Application.Services.Email;

using System.Threading;
using System.Threading.Tasks;

public interface IEmailNotificationService
{
    Task<bool> SendOtpEmailAsync(SendOtpEmailDto dto, CancellationToken cancellationToken = default);
    Task<bool> SendEmailAsync(SendGeneralEmailDto dto, CancellationToken cancellationToken = default);
}
