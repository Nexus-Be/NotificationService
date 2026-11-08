namespace Notification.Application.Services.OrderPayments;
public sealed record OrderPaymentDeliveryResponse(Guid Id,string Channel,string Status,string? ErrorCode);
public sealed record OrderPaymentBranchResponse(Guid NotificationId,Guid UserId,string NotificationStatus,bool InboxVisible,
    IReadOnlyList<OrderPaymentDeliveryResponse> Deliveries);
public sealed record OrderPaymentResponse(Guid SourceEventId,Guid OrderId,bool IsReplay,OrderPaymentBranchResponse Buyer,OrderPaymentBranchResponse Seller);
