namespace Notification.Application.Services.AuctionOrderPayments;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAuctionOrderPaymentRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? AuctionId { get; init; }
    [Required] public Guid? ResultId { get; init; }
    [Required] public Guid? WinningBidId { get; init; }
    [Required] public Guid? OrderId { get; init; }
    [Required] public Guid? ShopId { get; init; }
    [Required] public Guid? BuyerUserId { get; init; }
    [Required] public Guid? SellerUserId { get; init; }
    [Required,StringLength(100)] public string OrderNo { get; init; } = string.Empty;
    [Required] public decimal? TotalAmount { get; init; }
    [Required,RegularExpression("(?i)^VND$")] public string Currency { get; init; } = "VND";
    [Required,RegularExpression("(?i)^Paid$")] public string OrderStatus { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^Settled$")] public string AuctionStatus { get; init; } = string.Empty;
    [Required] public DateTimeOffset? SellerAcceptStartedAt { get; init; }
    [Required] public DateTimeOffset? PaidAt { get; init; }
    [Required] public DateTimeOffset? SellerAcceptDeadline { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
