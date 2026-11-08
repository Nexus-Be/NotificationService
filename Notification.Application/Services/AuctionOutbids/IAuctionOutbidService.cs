namespace Notification.Application.Services.AuctionOutbids;
using Notification.Application.Services.Notifications;
public interface IAuctionOutbidService
{
    Task<AuctionOutbidResponse> CreateAsync(CreateAuctionOutbidRequest request,CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default);
}
