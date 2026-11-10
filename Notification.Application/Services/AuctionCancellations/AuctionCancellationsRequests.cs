namespace Notification.Application.Services.AuctionCancellations;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAuctionCancellationRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? AuctionId { get; init; }
    [Required] public Guid? SellerUserId { get; init; }
    [Required] public Guid? RecipientUserId { get; init; }
    [Required,RegularExpression("(?i)^(Seller|Admin)$")] public string CancellationKind { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^(Seller|Participant)$")] public string RecipientKind { get; init; } = string.Empty;
    [Required,Range(0,int.MaxValue)] public int? BidCount { get; init; }
    [Required,StringLength(200)] public string AuctionTitle { get; init; } = string.Empty;
    [Required,StringLength(500)] public string Reason { get; init; } = string.Empty;
    [Required] public DateTimeOffset? CancelledAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
