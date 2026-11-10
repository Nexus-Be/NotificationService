namespace Notification.Application.Services.OrderSettlements;
using Notification.Application.Services.Notifications;
public interface IOrderSettlementService
{
    Task<OrderSettlementResponse> CreateAsync(CreateOrderSettlementRequest request,CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default);
}
