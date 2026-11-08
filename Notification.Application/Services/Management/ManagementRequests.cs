namespace Notification.Application.Services.Management;

using System.ComponentModel.DataAnnotations;

public record ManagementPageRequest
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}
public sealed record TemplateListRequest : ManagementPageRequest
{
    [StringLength(150)] public string? Code { get; init; }
    [StringLength(40)] public string? Channel { get; init; }
    [StringLength(40)] public string? NotificationType { get; init; }
    public bool? IsActive { get; init; }
}
public sealed record DeliveryListRequest : ManagementPageRequest
{
    public Guid? NotificationId { get; init; }
    public Guid? UserId { get; init; }
    [StringLength(40)] public string? Channel { get; init; }
    [StringLength(40)] public string? Status { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}
public sealed record CreateTemplateRequest
{
    [Required, StringLength(150)] public string Code { get; init; } = string.Empty;
    [Required, StringLength(20)] public string Locale { get; init; } = "vi-VN";
    [Required, StringLength(40)] public string NotificationType { get; init; } = string.Empty;
    [Required, StringLength(40)] public string ChannelCode { get; init; } = string.Empty;
    [Required, StringLength(500)] public string SubjectTemplate { get; init; } = string.Empty;
    [Required, StringLength(4000)] public string BodyTemplate { get; init; } = string.Empty;
    public bool IsMandatory { get; init; }
    public bool IsActive { get; init; } = true;
}
public sealed record UpdateTemplateRequest
{
    [Required] public DateTimeOffset? ExpectedUpdatedAt { get; init; }
    [Required, StringLength(500)] public string SubjectTemplate { get; init; } = string.Empty;
    [Required, StringLength(4000)] public string BodyTemplate { get; init; } = string.Empty;
    [Required] public bool? IsMandatory { get; init; }
    [Required] public bool? IsActive { get; init; }
}
public sealed record UpdateChannelRequest
{
    [Required, StringLength(150)] public string Name { get; init; } = string.Empty;
    [Required] public bool? IsDisabled { get; init; }
    [Required] public DateTimeOffset? ExpectedUpdatedAt { get; init; }
}
public sealed record RetryDeliveryRequest
{
    [Required] public Guid? RequestId { get; init; }
    [Required, Range(typeof(long), "1", "9223372036854775807")] public long? ExpectedRowVersion { get; init; }
    [Required, StringLength(500)] public string Reason { get; init; } = string.Empty;
}
