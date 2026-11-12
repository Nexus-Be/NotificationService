namespace Notification.Domain.Entities;
using Notification.Domain.Base;
using Notification.Domain.Enums;
public sealed class OrderCompletionDispatch : BaseEntity<Guid>
{
    public NotificationOrderKind OrderKind { get; set; }
    public Guid? AuctionId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid OrderId { get; set; }
    public required string RequestHash { get; set; }
    public Guid BuyerNotificationId { get; set; }
    public Guid SellerNotificationId { get; set; }
}
