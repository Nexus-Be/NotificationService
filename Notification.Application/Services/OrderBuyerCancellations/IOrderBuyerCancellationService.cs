namespace Notification.Application.Services.OrderBuyerCancellations;

using Notification.Application.Services.Notifications;
public interface IOrderBuyerCancellationService
{
    Task<OrderBuyerCancellationResponse> CreateAsync(CreateOrderBuyerCancellationRequest request, CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);
}
