namespace Notification.Domain.Entities;
using Notification.Domain.Base;
public sealed class AuctionCancellationRecipient : BaseEntity<Guid>
{
    public Guid SourceEventId { get; set; }
    public Guid UserId { get; set; }
    public Guid NotificationId { get; set; }
}
