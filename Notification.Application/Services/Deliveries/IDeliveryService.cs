namespace Notification.Application.Services.Deliveries;

public interface IDeliveryService
{
    /// <summary>Processes at most one due email delivery; returns false when no work is available.</summary>
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default);
}
