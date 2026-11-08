namespace Notification.Application.Services.AuctionOutbids;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAuctionOutbidRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? AuctionId { get; init; }
    [Required] public Guid? TriggerBidId { get; init; }
    [Required] public Guid? PreviousBidId { get; init; }
    [Required] public Guid? PreviousHighestBidderUserId { get; init; }
    [Required] public Guid? NewHighestBidderUserId { get; init; }
    [Required] public Guid? SellerUserId { get; init; }
    [Required,StringLength(200)] public string AuctionTitle { get; init; } = string.Empty;
    [Required] public decimal? PreviousAmount { get; init; }
    [Required] public decimal? NewAmount { get; init; }
    [Required,RegularExpression("(?i)^VND$")] public string Currency { get; init; } = "VND";
    [Required] public DateTimeOffset? OccurredAt { get; init; }
    [Required] public DateTimeOffset? AuctionEndsAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
