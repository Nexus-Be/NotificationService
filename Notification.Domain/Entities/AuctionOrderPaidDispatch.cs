namespace Notification.Domain.Entities;
using Notification.Domain.Base;
public sealed class AuctionOrderPaidDispatch : BaseEntity<Guid>
{
    public Guid AuctionId { get; set; }
    public Guid ResultId { get; set; }
    public Guid WinningBidId { get; set; }
    public Guid OrderId { get; set; }
    public required string RequestHash { get; set; }
    public Guid BuyerNotificationId { get; set; }
    public Guid SellerNotificationId { get; set; }
}
