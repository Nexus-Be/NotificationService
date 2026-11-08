namespace Notification.Infrastructure.Services.Push;

using Google.Apis.Auth.OAuth2;

public sealed class GoogleFcmAccessTokenProvider : IFcmAccessTokenProvider, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GoogleCredential? _credential;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_credential is null)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                _credential ??= (await GoogleCredential.GetApplicationDefaultAsync(cancellationToken))
                    .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
            }
            finally { _gate.Release(); }
        }
        return await ((ITokenAccess)_credential).GetAccessTokenForRequestAsync(null, cancellationToken);
    }

    public void Dispose() => _gate.Dispose();
}
