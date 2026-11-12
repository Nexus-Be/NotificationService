namespace Notification.Application.Services.OrderSettlements;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOrderSettlementRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? SettlementId { get; init; }
    [Required] public Guid? WalletCreditTransactionId { get; init; }
    [Required] public Guid? OrderId { get; init; }
    [Required] public Guid? SellerUserId { get; init; }
    public Guid? AuctionId { get; init; }
    [Required,RegularExpression("(?i)^(Regular|Auction)$")] public string OrderKind { get; init; } = string.Empty;
    [Required,StringLength(100)] public string OrderNo { get; init; } = string.Empty;
    [Required] public decimal? NetAmount { get; init; }
    [Required,RegularExpression("(?i)^VND$")] public string Currency { get; init; } = "VND";
    [Required,RegularExpression("(?i)^Completed$")] public string SettlementStatus { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^Completed$")] public string WalletCreditStatus { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^SellerWallet$")] public string SettlementDestination { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^Completed$")] public string OrderStatus { get; init; } = string.Empty;
    [Required] public DateTimeOffset? OrderCompletedAt { get; init; }
    [Required] public DateTimeOffset? WalletCreditedAt { get; init; }
    [Required] public DateTimeOffset? SettledAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
