namespace Notification.Application.Services.SellerDecisions;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateSellerDecisionRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? ApplicationId { get; init; }
    [Required] public Guid? UserId { get; init; }
    [Required,EmailAddress,StringLength(254)] public string Email { get; init; } = string.Empty;
    [StringLength(200)] public string? FullName { get; init; }
    [Required,StringLength(200)] public string ShopName { get; init; } = string.Empty;
    [Required,RegularExpression(@"(?i)^\s*(Approved|Rejected)\s*$")] public string Decision { get; init; } = string.Empty;
    [StringLength(1000)] public string? RejectionReason { get; init; }
    [Required] public DateTimeOffset? DecidedAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
