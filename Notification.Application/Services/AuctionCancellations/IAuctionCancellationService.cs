namespace Notification.Application.Services.AuctionCancellations;
using Notification.Application.Services.Notifications;
public interface IAuctionCancellationService
{
    Task<AuctionCancellationResponse> CreateAsync(CreateAuctionCancellationRequest request,CancellationToken cancellationToken = default);
    Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default);
}
