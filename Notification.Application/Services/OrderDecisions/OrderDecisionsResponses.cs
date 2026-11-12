namespace Notification.Application.Services.OrderDecisions;
public sealed record OrderDecisionDeliveryResponse(Guid Id,string Channel,string Status,string? ErrorCode);
public sealed record OrderDecisionBranchResponse(Guid NotificationId,Guid UserId,string NotificationStatus,bool InboxVisible,
    IReadOnlyList<OrderDecisionDeliveryResponse> Deliveries);
public sealed record OrderDecisionResponse(Guid SourceEventId,Guid OrderId,bool IsReplay,OrderDecisionBranchResponse Buyer,OrderDecisionBranchResponse Seller);
