namespace Notification.Application.Services.OrderPaymentTimeouts;

using Notification.Application.Services.Notifications;
public interface IOrderPaymentTimeoutService
{
    Task<OrderPaymentTimeoutResponse> CreateAsync(CreateOrderPaymentTimeoutRequest request, CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);
}
