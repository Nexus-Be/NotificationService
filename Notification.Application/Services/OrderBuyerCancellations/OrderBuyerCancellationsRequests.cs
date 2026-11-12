namespace Notification.Application.Services.OrderBuyerCancellations;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOrderBuyerCancellationRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? OrderId { get; init; }
    [Required] public Guid? ShopId { get; init; }
    [Required] public Guid? BuyerUserId { get; init; }
    [Required] public Guid? SellerUserId { get; init; }
    [Required, RegularExpression("(?i)^Regular$")] public string OrderKind { get; init; } = string.Empty;
    [Required, StringLength(100)] public string OrderNo { get; init; } = string.Empty;
    [Required, RegularExpression("(?i)^(Paid|Shipping)$")] public string PreviousOrderStatus { get; init; } = string.Empty;
    [Required, RegularExpression("(?i)^Cancelled$")] public string OrderStatus { get; init; } = string.Empty;
    [Required, RegularExpression("(?i)^Buyer$")] public string CancelledBy { get; init; } = string.Empty;
    [Required, RegularExpression("(?i)^NotHandedOver$")] public string HandoverStatus { get; init; } = string.Empty;
    [Required, StringLength(500)] public string Reason { get; init; } = string.Empty;
    [Required] public DateTimeOffset? OrderCreatedAt { get; init; }
    [Required] public DateTimeOffset? PaidAt { get; init; }
    [Required] public DateTimeOffset? CancelledAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
