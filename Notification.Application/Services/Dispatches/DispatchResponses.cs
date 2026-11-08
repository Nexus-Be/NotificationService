namespace Notification.Application.Services.Dispatches;

public sealed record DispatchResponse(Guid NotificationId, Guid DeliveryId, string Channel,
    string NotificationStatus, string DeliveryStatus, bool IsReplay)
{
    public IReadOnlyList<DispatchDeliveryResponse> Deliveries { get; init; } = Array.Empty<DispatchDeliveryResponse>();
}

public sealed record DispatchDeliveryResponse(Guid DeliveryId, string Status);
