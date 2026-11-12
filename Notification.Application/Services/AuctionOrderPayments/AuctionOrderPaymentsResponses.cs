namespace Notification.Application.Services.AuctionOrderPayments;
public sealed record AuctionOrderPaymentDeliveryResponse(Guid Id,string Channel,string Status,string? ErrorCode);
public sealed record AuctionOrderPaymentBranchResponse(Guid NotificationId,Guid UserId,string NotificationStatus,bool InboxVisible,
    IReadOnlyList<AuctionOrderPaymentDeliveryResponse> Deliveries);
public sealed record AuctionOrderPaymentResponse(Guid SourceEventId,Guid OrderId,bool IsReplay,AuctionOrderPaymentBranchResponse Buyer,AuctionOrderPaymentBranchResponse Seller);
