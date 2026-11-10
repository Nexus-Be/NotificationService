namespace Notification.Application.Services.AuctionOrderPayments;
using Notification.Application.Services.Notifications;
public interface IAuctionOrderPaymentService
{
    Task<AuctionOrderPaymentResponse> CreateAsync(CreateAuctionOrderPaymentRequest request,CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default);
}
