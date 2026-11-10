namespace Notification.Application.Services.OrderCompletions;
public sealed record OrderCompletionDeliveryResponse(Guid Id,string Channel,string Status,string? ErrorCode);
public sealed record OrderCompletionBranchResponse(Guid NotificationId,Guid UserId,string NotificationStatus,bool InboxVisible,
    IReadOnlyList<OrderCompletionDeliveryResponse> Deliveries);
public sealed record OrderCompletionResponse(Guid SourceEventId,Guid OrderId,bool IsReplay,OrderCompletionBranchResponse Buyer,OrderCompletionBranchResponse Seller);
