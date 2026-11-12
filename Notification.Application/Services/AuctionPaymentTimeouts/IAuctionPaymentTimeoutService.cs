namespace Notification.Application.Services.AuctionPaymentTimeouts;
using Notification.Application.Services.Notifications;
public interface IAuctionPaymentTimeoutService
{
    Task<AuctionPaymentTimeoutResponse> CreateAsync(CreateAuctionPaymentTimeoutRequest request,CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default);
}
