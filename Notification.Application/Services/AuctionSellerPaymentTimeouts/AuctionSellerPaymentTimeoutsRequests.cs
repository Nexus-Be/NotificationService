namespace Notification.Application.Services.AuctionSellerPaymentTimeouts;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAuctionSellerPaymentTimeoutRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? AuctionId { get; init; }
    [Required] public Guid? ResultId { get; init; }
    [Required] public Guid? WinningBidId { get; init; }
    [Required] public Guid? OrderId { get; init; }
    [Required] public Guid? WinnerUserId { get; init; }
    [Required] public Guid? SellerUserId { get; init; }
    [Required,StringLength(200)] public string AuctionTitle { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^Cancelled$")] public string OrderStatus { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^Failed$")] public string AuctionStatus { get; init; } = string.Empty;
    [Required] public DateTimeOffset? SettledAt { get; init; }
    [Required] public DateTimeOffset? PaymentDeadline { get; init; }
    [Required] public DateTimeOffset? TimedOutAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
