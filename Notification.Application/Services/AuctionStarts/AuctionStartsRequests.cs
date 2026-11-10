namespace Notification.Application.Services.AuctionStarts;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAuctionStartRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? AuctionId { get; init; }
    [Required] public Guid? SellerUserId { get; init; }
    [Required,StringLength(200)] public string AuctionTitle { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^Active$")] public string AuctionStatus { get; init; } = string.Empty;
    [Required] public DateTimeOffset? ScheduledStartAt { get; init; }
    [Required] public DateTimeOffset? StartedAt { get; init; }
    [Required] public DateTimeOffset? AuctionEndsAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
