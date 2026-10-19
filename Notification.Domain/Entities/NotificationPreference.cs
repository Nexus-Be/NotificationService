using Notification.Domain.Base;
using Notification.Domain.Enums;

namespace Notification.Domain.Entities;

public class NotificationPreference : BaseEntity<Guid>
{
    public required Guid UserId { get; set; }
    public required NotificationType NotificationType { get; set; }
    public required ChannelType ChannelCode { get; set; }
    public bool IsDisabled { get; set; }

    // Navigation
    public NotificationChannel? Channel { get; set; }
}
