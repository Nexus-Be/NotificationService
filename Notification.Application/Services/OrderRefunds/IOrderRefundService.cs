namespace Notification.Application.Services.OrderRefunds;
using Notification.Application.Services.Notifications;
public interface IOrderRefundService
{
    Task<OrderRefundResponse> CreateAsync(CreateOrderRefundRequest request,CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default);
}
