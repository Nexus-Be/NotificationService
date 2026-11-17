namespace Notification.Application.Services.AuctionLifecycle;

public interface IAuctionLifecycleService
{
    Task<AuctionLifecycleResponse> CreateAsync(AuctionLifecycleRequest request, CancellationToken ct);
}
