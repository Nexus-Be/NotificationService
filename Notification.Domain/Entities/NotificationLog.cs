using Notification.Domain.Base;
using Notification.Domain.Enums;

namespace Notification.Domain.Entities;

public class NotificationLog : BaseEntity<Guid>
{
    public Guid? NotificationId { get; set; }
    public Guid? DeliveryId { get; set; }
    public required LogAction Action { get; set; }
    public LogLevel Level { get; set; } = LogLevel.Info;
    public string? Message { get; set; }
    public string? Metadata { get; set; }

    // Navigation
    public NotificationRecord? Notification { get; set; }
    public NotificationDelivery? Delivery { get; set; }
}
