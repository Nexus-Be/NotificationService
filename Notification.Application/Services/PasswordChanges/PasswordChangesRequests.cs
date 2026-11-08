namespace Notification.Application.Services.PasswordChanges;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreatePasswordChangeRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? UserId { get; init; }
    [Required,EmailAddress,StringLength(254)] public string Email { get; init; } = string.Empty;
    [StringLength(200)] public string? FullName { get; init; }
    [Required,RegularExpression(@"(?i)^\s*(Changed|Reset)\s*$")] public string Kind { get; init; } = string.Empty;
    [Required] public DateTimeOffset? ChangedAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
