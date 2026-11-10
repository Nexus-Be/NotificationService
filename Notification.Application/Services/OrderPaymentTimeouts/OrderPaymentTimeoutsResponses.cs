namespace Notification.Application.Services.OrderPaymentTimeouts;

public sealed record OrderPaymentTimeoutDeliveryResponse(Guid Id, string Channel, string Status, string? ErrorCode);
public sealed record OrderPaymentTimeoutResponse(Guid SourceEventId, Guid OrderId, Guid NotificationId, Guid UserId, string NotificationStatus, bool InboxVisible, bool IsReplay, IReadOnlyList<OrderPaymentTimeoutDeliveryResponse> Deliveries);
