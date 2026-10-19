using Notification.Domain.Base;
using Notification.Domain.Enums;

namespace Notification.Domain.Entities;

public class NotificationDelivery : BaseEntity<Guid>
{
    public required Guid NotificationId { get; set; }
    public required ChannelType ChannelCode { get; set; }
    public required string Destination { get; set; }
    public required DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public long RowVersion { get; set; }

    // Navigation
    public NotificationRecord? Notification { get; set; }
    public NotificationChannel? Channel { get; set; }
    public ICollection<NotificationLog> Logs { get; set; } = new List<NotificationLog>();
}
