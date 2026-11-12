namespace Notification.Domain.Entities;

using Notification.Domain.Base;
public sealed class OrderPaymentTimeoutDispatch : BaseEntity<Guid>
{
    public Guid OrderId { get; set; }
    public Guid NotificationId { get; set; }
    public required string RequestHash { get; set; }
}
