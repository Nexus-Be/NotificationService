namespace Notification.Application.Services.OrderPayments;
using Notification.Application.Services.Notifications;
public interface IOrderPaymentService
{
    Task<OrderPaymentResponse> CreateAsync(CreateOrderPaymentRequest request,CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default);
}
