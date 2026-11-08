namespace Notification.Application.Services.AuctionWinners;
using Notification.Application.Services.Notifications;
public interface IAuctionWinnerService
{
    Task<AuctionWinnerResponse> CreateAsync(CreateAuctionWinnerRequest request,CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default);
}
