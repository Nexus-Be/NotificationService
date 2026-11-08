namespace Notification.Application.Services.AuctionWinners;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAuctionWinnerRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? AuctionId { get; init; }
    [Required] public Guid? ResultId { get; init; }
    [Required] public Guid? WinningBidId { get; init; }
    [Required] public Guid? WinnerUserId { get; init; }
    [Required] public Guid? SellerUserId { get; init; }
    public Guid? OrderId { get; init; }
    [Required,EmailAddress,StringLength(254)] public string WinnerEmail { get; init; } = string.Empty;
    [StringLength(200)] public string? WinnerFullName { get; init; }
    [Required,StringLength(200)] public string AuctionTitle { get; init; } = string.Empty;
    [Required] public decimal? WinningAmount { get; init; }
    [Required,RegularExpression("(?i)^VND$")] public string Currency { get; init; } = "VND";
    [Required] public DateTimeOffset? SettledAt { get; init; }
    [Required] public DateTimeOffset? PaymentDeadline { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
