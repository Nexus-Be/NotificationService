namespace Notification.Application.Services.OrderBuyerCancellations;

public sealed record OrderBuyerCancellationDeliveryResponse(Guid Id, string Channel, string Status, string? ErrorCode);
public sealed record OrderBuyerCancellationBranchResponse(Guid NotificationId, Guid UserId, string NotificationStatus, bool InboxVisible,
    IReadOnlyList<OrderBuyerCancellationDeliveryResponse> Deliveries);
public sealed record OrderBuyerCancellationResponse(Guid SourceEventId, Guid OrderId, bool IsReplay, OrderBuyerCancellationBranchResponse Buyer, OrderBuyerCancellationBranchResponse Seller);
