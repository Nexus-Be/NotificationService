namespace Notification.Domain.Entities;
using Notification.Domain.Base;
public sealed class AuctionOutbidDispatch : BaseEntity<Guid>
{
    public Guid AuctionId { get; set; }
    public Guid TriggerBidId { get; set; }
    public Guid PreviousBidId { get; set; }
    public Guid NotificationId { get; set; }
    public required string RequestHash { get; set; }
}
