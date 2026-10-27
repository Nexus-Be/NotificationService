using Notification.Domain.Base;
using Notification.Domain.Enums;

namespace Notification.Domain.Entities;

public class NotificationRecord : BaseEntity<Guid>
{
    public required Guid UserId { get; set; }
    public Guid? SourceEventId { get; set; }
    public Guid? TemplateId { get; set; }
    public NotificationType? NotificationType { get; set; }
    public ChannelScope ChannelScope { get; set; } = ChannelScope.All;
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? Payload { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public required NotificationStatus Status { get; set; } = NotificationStatus.Pending;

    public void MarkAsRead()
    {
        if (IsRead)
            return;
        var now = DateTimeOffset.UtcNow;
        IsRead = true;
        ReadAt = now;
        UpdatedAt = now;
    }

    // Navigation
    public NotificationTemplate? Template { get; set; }
    public ICollection<NotificationDelivery> Deliveries { get; set; } = new List<NotificationDelivery>();
    public ICollection<NotificationLog> Logs { get; set; } = new List<NotificationLog>();
}
