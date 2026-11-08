namespace Notification.Application.Services.Dispatches;

public interface IDispatchService
{
    Task<DispatchResponse> CreateAsync(CreateDispatchRequest request, CancellationToken cancellationToken = default);
}
