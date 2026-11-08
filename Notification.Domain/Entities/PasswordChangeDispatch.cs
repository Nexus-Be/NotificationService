namespace Notification.Domain.Entities;

using Notification.Domain.Base;

public sealed class PasswordChangeDispatch : BaseEntity<Guid>
{
    public required string RequestHash { get; set; }
    public Guid NotificationId { get; set; }
}
