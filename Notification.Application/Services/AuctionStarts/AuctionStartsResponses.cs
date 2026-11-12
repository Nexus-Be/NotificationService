namespace Notification.Application.Services.AuctionStarts;
public sealed record AuctionStartDeliveryResponse(Guid Id,string Channel,string Status,string? ErrorCode);
public sealed record AuctionStartResponse(Guid SourceEventId,Guid AuctionId,Guid NotificationId,Guid UserId,
    string NotificationStatus,bool InboxVisible,bool IsReplay,IReadOnlyList<AuctionStartDeliveryResponse> Deliveries);
