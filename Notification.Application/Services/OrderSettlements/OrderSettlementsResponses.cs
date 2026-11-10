namespace Notification.Application.Services.OrderSettlements;
public sealed record OrderSettlementDeliveryResponse(Guid Id,string Channel,string Status,string? ErrorCode);
public sealed record OrderSettlementResponse(Guid SourceEventId,Guid SettlementId,Guid OrderId,Guid NotificationId,Guid UserId,string NotificationStatus,bool InboxVisible,bool IsReplay,IReadOnlyList<OrderSettlementDeliveryResponse> Deliveries);
