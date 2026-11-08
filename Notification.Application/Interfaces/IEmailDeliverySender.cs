namespace Notification.Application.Interfaces;

using Notification.Application.Services.Deliveries;

public interface IEmailDeliverySender
{
    Task<EmailDeliveryResult> SendAsync(EmailDeliveryMessage message, CancellationToken cancellationToken = default);
}
