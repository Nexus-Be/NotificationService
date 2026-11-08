namespace Notification.Application.Interfaces;

using Notification.Application.Services.Deliveries;

public interface IPushDeliverySender
{
    Task<PushDeliveryResult> SendAsync(PushDeliveryMessage message, CancellationToken cancellationToken = default);
}
