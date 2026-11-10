namespace Notification.Application.Services.AuctionSellerPaymentTimeouts;
using Notification.Application.Services.Notifications;
public interface IAuctionSellerPaymentTimeoutService
{
    Task<AuctionSellerPaymentTimeoutResponse> CreateAsync(CreateAuctionSellerPaymentTimeoutRequest request,CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default);
}
