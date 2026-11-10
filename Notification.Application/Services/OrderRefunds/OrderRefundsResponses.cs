namespace Notification.Application.Services.OrderRefunds;
public sealed record OrderRefundDeliveryResponse(Guid Id,string Channel,string Status,string? ErrorCode);
public sealed record OrderRefundResponse(Guid SourceEventId,Guid RefundId,Guid OrderId,Guid NotificationId,Guid UserId,string NotificationStatus,bool InboxVisible,bool IsReplay,IReadOnlyList<OrderRefundDeliveryResponse> Deliveries);
