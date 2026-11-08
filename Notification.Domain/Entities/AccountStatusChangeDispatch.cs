namespace Notification.Domain.Entities;
using Notification.Domain.Base;

public sealed class AccountStatusChangeDispatch : BaseEntity<Guid>
{
    public Guid UserId { get; set; }
    public long StatusVersion { get; set; }
    public required string RequestHash { get; set; }
    public Guid NotificationId { get; set; }
}
