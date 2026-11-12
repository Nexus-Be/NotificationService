namespace Notification.Application.Services.AuctionCancellations;
public sealed record AuctionCancellationDeliveryResponse(Guid Id,string Channel,string Status,string? ErrorCode);
public sealed record AuctionCancellationResponse(Guid SourceEventId,Guid AuctionId,Guid NotificationId,Guid UserId,
    string NotificationStatus,bool InboxVisible,bool IsReplay,IReadOnlyList<AuctionCancellationDeliveryResponse> Deliveries);
