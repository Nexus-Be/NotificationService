namespace Notification.Application.Services.OrderCompletions;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOrderCompletionRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? OrderId { get; init; }
    [Required] public Guid? ShopId { get; init; }
    [Required] public Guid? ShipmentId { get; init; }
    [Required] public Guid? BuyerUserId { get; init; }
    [Required] public Guid? SellerUserId { get; init; }
    public Guid? AuctionId { get; init; }
    [Required,RegularExpression("(?i)^(Regular|Auction)$")] public string OrderKind { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^Completed$")] public string OrderStatus { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^Delivered$")] public string DeliveryStatus { get; init; } = string.Empty;
    [Required,StringLength(100)] public string OrderNo { get; init; } = string.Empty;
    [Required] public DateTimeOffset? PaidAt { get; init; }
    [Required] public DateTimeOffset? DeliveredAt { get; init; }
    [Required] public DateTimeOffset? CompletedAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
