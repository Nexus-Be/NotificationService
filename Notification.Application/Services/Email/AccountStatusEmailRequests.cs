namespace Notification.Application.Services.Email;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SendUserBannedEmailRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = string.Empty;
    [StringLength(200)] public string? FullName { get; init; }
    [Required, StringLength(1000)] public string Reason { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SendUserUnbannedEmailRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = string.Empty;
    [StringLength(200)] public string? FullName { get; init; }
    [StringLength(1000)] public string? Reason { get; init; }
}
