namespace Notification.Application.Services.AuctionWinners;
public sealed record AuctionWinnerDeliveryResponse(Guid Id,string Channel,string Status,string? ErrorCode);
public sealed record AuctionWinnerResponse(Guid SourceEventId,Guid AuctionId,Guid ResultId,Guid NotificationId,Guid UserId,
    string NotificationStatus,bool InboxVisible,bool IsReplay,IReadOnlyList<AuctionWinnerDeliveryResponse> Deliveries);
