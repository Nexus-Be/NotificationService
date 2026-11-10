namespace Notification.Application.Services.AuctionStarts;
using Notification.Application.Services.Notifications;
public interface IAuctionStartService
{
    Task<AuctionStartResponse> CreateAsync(CreateAuctionStartRequest request,CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default);
}
