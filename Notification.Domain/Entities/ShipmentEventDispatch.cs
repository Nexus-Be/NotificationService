namespace Notification.Domain.Entities;
using Notification.Domain.Base;
public sealed class ShipmentEventDispatch : BaseEntity<Guid>
{
    public Guid ShipmentId { get; set; }
    public long SourceVersion { get; set; }
    public required string RequestHash { get; set; }
    public string? SuppressionReason { get; set; }
    public Guid? BuyerNotificationId { get; set; }
    public Guid? SellerNotificationId { get; set; }
}
