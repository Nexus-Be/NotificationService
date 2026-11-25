namespace Notification.Application.Services.WithdrawalEvents;

public interface IWithdrawalEventService
{
    Task<WithdrawalEventResponse> CreateAsync(WithdrawalEventRequest request, CancellationToken ct);
}
