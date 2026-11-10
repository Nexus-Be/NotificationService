namespace Notification.Application.Services.AuctionSellerPaymentTimeouts;
public sealed record AuctionSellerPaymentTimeoutDeliveryResponse(Guid Id,string Channel,string Status,string? ErrorCode);
public sealed record AuctionSellerPaymentTimeoutResponse(Guid SourceEventId,Guid AuctionId,Guid OrderId,Guid NotificationId,Guid UserId,
    string NotificationStatus,bool InboxVisible,bool IsReplay,IReadOnlyList<AuctionSellerPaymentTimeoutDeliveryResponse> Deliveries);
