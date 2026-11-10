namespace Notification.Domain.Entities;
using Notification.Domain.Base;
using Notification.Domain.Enums;
public sealed class ShipmentNotificationStream : BaseEntity<Guid>
{
    public required string HeaderHash { get; set; }
    public long LastVersion { get; set; }
    public DateTimeOffset LastOccurredAt { get; set; }
    public ShipmentNotificationStatus LastStatus { get; set; }
}
