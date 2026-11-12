namespace Notification.Application.Services.ShipmentEvents;
using Notification.Application.Services.Notifications;
public interface IShipmentEventService
{
    Task<ShipmentEventResponse> CreateAsync(CreateShipmentEventRequest request,CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default);
}
