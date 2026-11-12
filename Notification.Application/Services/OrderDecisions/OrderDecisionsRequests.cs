namespace Notification.Application.Services.OrderDecisions;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOrderDecisionRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? OrderId { get; init; }
    [Required] public Guid? ShopId { get; init; }
    [Required] public Guid? BuyerUserId { get; init; }
    [Required] public Guid? SellerUserId { get; init; }
    public Guid? AuctionId { get; init; }
    [Required,RegularExpression("(?i)^(Regular|Auction)$")] public string OrderKind { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^(Accepted|Rejected|AcceptanceTimedOut)$")] public string Outcome { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^(Shipping|Cancelled)$")] public string OrderStatus { get; init; } = string.Empty;
    [Required,StringLength(100)] public string OrderNo { get; init; } = string.Empty;
    [StringLength(500)] public string? Reason { get; init; }
    [Required] public DateTimeOffset? PaidAt { get; init; }
    [Required] public DateTimeOffset? SellerAcceptStartedAt { get; init; }
    [Required] public DateTimeOffset? SellerAcceptDeadline { get; init; }
    [Required] public DateTimeOffset? DecidedAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
