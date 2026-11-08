namespace Notification.Infrastructure.Services.Push;

public interface IFcmAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
