namespace Notification.Domain.Entities;
using Notification.Domain.Base;
using Notification.Domain.Enums;
public sealed class OrderSettlementDispatch : BaseEntity<Guid>
{
    public Guid SettlementId { get; set; }
    public Guid WalletCreditTransactionId { get; set; }
    public Guid OrderId { get; set; }
    public NotificationOrderKind OrderKind { get; set; }
    public Guid? AuctionId { get; set; }
    public Guid NotificationId { get; set; }
    public required string RequestHash { get; set; }
}
