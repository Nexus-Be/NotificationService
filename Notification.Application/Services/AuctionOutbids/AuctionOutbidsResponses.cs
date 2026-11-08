namespace Notification.Application.Services.AuctionOutbids;
public sealed record AuctionOutbidDeliveryResponse(Guid Id,string Channel,string Status,string? ErrorCode);
public sealed record AuctionOutbidResponse(Guid SourceEventId,Guid AuctionId,Guid NotificationId,Guid UserId,
    string NotificationStatus,bool InboxVisible,bool IsReplay,IReadOnlyList<AuctionOutbidDeliveryResponse> Deliveries);
