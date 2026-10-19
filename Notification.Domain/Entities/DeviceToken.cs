using Notification.Domain.Base;
using Notification.Domain.Enums;

namespace Notification.Domain.Entities;

public class DeviceToken : BaseEntity<Guid>
{
    public required Guid UserId { get; set; }
    public required DevicePlatform Platform { get; set; }
    public required string Token { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastSeenAt { get; set; }
}
