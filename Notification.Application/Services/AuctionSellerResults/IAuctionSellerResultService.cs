namespace Notification.Application.Services.AuctionSellerResults;
using Notification.Application.Services.Notifications;
public interface IAuctionSellerResultService
{
    Task<AuctionSellerResultResponse> CreateAsync(CreateAuctionSellerResultRequest request,CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default);
}
