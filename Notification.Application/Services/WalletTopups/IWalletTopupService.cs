namespace Notification.Application.Services.WalletTopups;

public interface IWalletTopupService
{
    Task<WalletTopupResponse> CreateAsync(WalletTopupRequest request, CancellationToken cancellationToken = default);
}
