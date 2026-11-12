namespace Notification.Application.Services.AuctionSellerResults;
public sealed record AuctionSellerResultDeliveryResponse(Guid Id,string Channel,string Status,string? ErrorCode);
public sealed record AuctionSellerResultResponse(Guid SourceEventId,Guid AuctionId,Guid ResultId,Guid NotificationId,Guid UserId,
    string NotificationStatus,bool InboxVisible,bool IsReplay,IReadOnlyList<AuctionSellerResultDeliveryResponse> Deliveries);
