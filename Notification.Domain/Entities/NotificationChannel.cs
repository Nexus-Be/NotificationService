using Notification.Domain.Base;
using Notification.Domain.Enums;

namespace Notification.Domain.Entities;

public class NotificationChannel : AuditableEntity<Guid>
{
    public required ChannelType Code { get; set; }
    public required string Name { get; set; }
    public ChannelProvider? Provider { get; set; }
    public string? Config { get; set; }
    public bool IsDisabled { get; set; }

    // Navigation
    public ICollection<NotificationTemplate> Templates { get; set; } = new List<NotificationTemplate>();
    public ICollection<NotificationPreference> Preferences { get; set; } = new List<NotificationPreference>();
    public ICollection<NotificationDelivery> Deliveries { get; set; } = new List<NotificationDelivery>();
}
