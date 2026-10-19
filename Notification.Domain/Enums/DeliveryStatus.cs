namespace Notification.Domain.Enums;

public enum DeliveryStatus
{
    Pending = 0,
    Sending = 1,
    Sent = 2,
    Failed = 3,
    Skipped = 4
}
