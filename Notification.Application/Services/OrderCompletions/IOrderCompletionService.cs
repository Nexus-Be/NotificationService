namespace Notification.Application.Services.OrderCompletions;
using Notification.Application.Services.Notifications;
public interface IOrderCompletionService
{
    Task<OrderCompletionResponse> CreateAsync(CreateOrderCompletionRequest request,CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default);
}
