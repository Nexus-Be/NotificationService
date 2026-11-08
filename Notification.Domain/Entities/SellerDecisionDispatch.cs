namespace Notification.Domain.Entities;

using Notification.Domain.Base;

public sealed class SellerDecisionDispatch : BaseEntity<Guid>
{
    public Guid ApplicationId { get; set; }
    public required string RequestHash { get; set; }
    public Guid NotificationId { get; set; }
}
