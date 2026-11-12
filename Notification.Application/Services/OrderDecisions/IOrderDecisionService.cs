namespace Notification.Application.Services.OrderDecisions;
using Notification.Application.Services.Notifications;
public interface IOrderDecisionService
{
    Task<OrderDecisionResponse> CreateAsync(CreateOrderDecisionRequest request,CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default);
}
