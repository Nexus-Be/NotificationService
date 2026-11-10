namespace Notification.Application.Services.OrderRefunds;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOrderRefundRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? RefundId { get; init; }
    [Required] public Guid? WalletCreditTransactionId { get; init; }
    [Required] public Guid? OrderId { get; init; }
    [Required] public Guid? BuyerUserId { get; init; }
    public Guid? AuctionId { get; init; }
    [Required,RegularExpression("(?i)^(Regular|Auction)$")] public string OrderKind { get; init; } = string.Empty;
    [Required,StringLength(100)] public string OrderNo { get; init; } = string.Empty;
    [Required] public decimal? RefundAmount { get; init; }
    [Required,RegularExpression("(?i)^VND$")] public string Currency { get; init; } = "VND";
    [Required,RegularExpression("(?i)^Completed$")] public string RefundStatus { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^Completed$")] public string WalletCreditStatus { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^BuyerWallet$")] public string RefundDestination { get; init; } = string.Empty;
    [Required] public DateTimeOffset? RequestedAt { get; init; }
    [Required] public DateTimeOffset? WalletCreditedAt { get; init; }
    [Required] public DateTimeOffset? CompletedAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
