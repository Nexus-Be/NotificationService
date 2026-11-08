namespace Notification.Application.Services.PasswordChanges;

public interface IPasswordChangeService
{
    Task<PasswordChangeResponse> CreateAsync(CreatePasswordChangeRequest request,CancellationToken cancellationToken = default);
}
