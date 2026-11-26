namespace Notification.Application.Services.WalletEvents;

using Notification.Application.Services.ServiceEvents;

public interface IWalletEventService
{
    Task<ServiceEventResponse> CreateWithdrawalAsync(WithdrawalEventRequest request, CancellationToken ct = default);
    Task<ServiceEventResponse> CreateDepositAsync(WalletDepositEventRequest request, CancellationToken ct = default);
}
