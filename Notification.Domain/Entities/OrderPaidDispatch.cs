namespace Notification.Domain.Entities;
using Notification.Domain.Base;
public sealed class OrderPaidDispatch : BaseEntity<Guid>
{
    public Guid OrderId { get; set; }
    public required string RequestHash { get; set; }
    public Guid BuyerNotificationId { get; set; }
    public Guid SellerNotificationId { get; set; }
}
