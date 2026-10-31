namespace Notification.Application.Interfaces;

using System.Threading;
using System.Threading.Tasks;
using Notification.Application.Services.Email;

public interface IEmailBackgroundQueue
{
    ValueTask QueueOtpEmailAsync(SendOtpEmailDto dto, CancellationToken cancellationToken = default);
    ValueTask<SendOtpEmailDto> DequeueOtpEmailAsync(CancellationToken cancellationToken);
}
