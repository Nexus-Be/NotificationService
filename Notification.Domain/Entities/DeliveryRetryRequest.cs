namespace Notification.Domain.Entities;

using Notification.Domain.Base;

public sealed class DeliveryRetryRequest : BaseEntity<Guid>
{
    public Guid DeliveryId { get; set; }
    public Guid ActorId { get; set; }
    public required string Reason { get; set; }
    public long ExpectedRowVersion { get; set; }
    public long AcceptedRowVersion { get; set; }
    public int PreviousAttemptCount { get; set; }
    public NotificationDelivery? Delivery { get; set; }
}
