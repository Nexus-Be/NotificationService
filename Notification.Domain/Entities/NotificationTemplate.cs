using Notification.Domain.Base;
using Notification.Domain.Enums;

namespace Notification.Domain.Entities;

public class NotificationTemplate : AuditableEntity<Guid>
{
    public required string Code { get; set; }
    public required NotificationType NotificationType { get; set; }
    public required ChannelType ChannelCode { get; set; }
    public string? SubjectTemplate { get; set; }
    public string? BodyTemplate { get; set; }
    public bool IsMandatory { get; set; }
    public string Locale { get; set; } = "vi-VN";
    public bool IsActive { get; set; } = true;

    // Navigation
    public NotificationChannel? Channel { get; set; }
    public ICollection<NotificationRecord> Notifications { get; set; } = new List<NotificationRecord>();
}
