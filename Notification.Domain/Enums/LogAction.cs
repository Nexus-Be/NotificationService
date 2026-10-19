namespace Notification.Domain.Enums;

public enum LogAction
{
    Created = 0,
    Rendered = 1,
    Dispatching = 2,
    Sent = 3,
    Failed = 4,
    Retry = 5,
    Suppressed = 6
}
