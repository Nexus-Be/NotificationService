namespace Notification.Application.Services.AccountStatusChanges;

public interface IAccountStatusChangeService
{
    Task<AccountStatusChangeResponse> CreateAsync(CreateAccountStatusChangeRequest request, CancellationToken cancellationToken = default);
}
