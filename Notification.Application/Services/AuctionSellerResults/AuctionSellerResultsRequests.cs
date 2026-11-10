namespace Notification.Application.Services.AuctionSellerResults;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAuctionSellerResultRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? AuctionId { get; init; }
    [Required] public Guid? ResultId { get; init; }
    [Required] public Guid? SellerUserId { get; init; }
    [Required,RegularExpression("(?i)^(WinnerSelected|NoBids)$")] public string Outcome { get; init; } = string.Empty;
    [Required,StringLength(200)] public string AuctionTitle { get; init; } = string.Empty;
    [Required] public DateTimeOffset? DecidedAt { get; init; }
    public Guid? WinningBidId { get; init; }
    public Guid? WinnerUserId { get; init; }
    public Guid? OrderId { get; init; }
    public decimal? WinningAmount { get; init; }
    [StringLength(3)] public string? Currency { get; init; }
    public DateTimeOffset? PaymentDeadline { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
