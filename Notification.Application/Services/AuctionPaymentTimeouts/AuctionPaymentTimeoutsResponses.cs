namespace Notification.Application.Services.AuctionPaymentTimeouts;
public sealed record AuctionPaymentTimeoutDeliveryResponse(Guid Id,string Channel,string Status,string? ErrorCode);
public sealed record AuctionPaymentTimeoutResponse(Guid SourceEventId,Guid AuctionId,Guid OrderId,Guid NotificationId,Guid UserId,
    string NotificationStatus,bool InboxVisible,bool IsReplay,IReadOnlyList<AuctionPaymentTimeoutDeliveryResponse> Deliveries);
