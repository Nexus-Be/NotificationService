namespace Notification.Application.Services.AuctionActivity;

public interface IAuctionActivityService
{
    Task<AuctionActivityResponse> CreateAsync(IAuctionNotificationRequest request, CancellationToken ct);
}
