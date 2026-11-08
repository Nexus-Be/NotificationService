namespace Notification.Application.Services.AccountStatusChanges;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAccountStatusChangeRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? UserId { get; init; }
    [Required,Range(typeof(long),"1","9223372036854775807")] public long? StatusVersion { get; init; }
    [Required,EmailAddress,StringLength(254)] public string Email { get; init; } = string.Empty;
    [StringLength(200)] public string? FullName { get; init; }
    [Required,RegularExpression(@"(?i)^\s*(Banned|Unbanned)\s*$")] public string Change { get; init; } = string.Empty;
    [Required,StringLength(1000)] public string Reason { get; init; } = string.Empty;
    [Required] public DateTimeOffset? ChangedAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
