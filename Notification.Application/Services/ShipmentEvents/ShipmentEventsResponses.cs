namespace Notification.Application.Services.ShipmentEvents;
public sealed record ShipmentEventDeliveryResponse(Guid Id,string Channel,string Status,string? ErrorCode);
public sealed record ShipmentEventBranchResponse(Guid NotificationId,Guid UserId,string NotificationStatus,bool InboxVisible,IReadOnlyList<ShipmentEventDeliveryResponse> Deliveries);
public sealed record ShipmentEventResponse(Guid SourceEventId,Guid ShipmentId,long SourceVersion,bool IsReplay,string? SuppressionReason,ShipmentEventBranchResponse? Buyer,ShipmentEventBranchResponse? Seller);
