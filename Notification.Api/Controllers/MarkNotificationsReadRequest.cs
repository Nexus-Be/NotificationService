namespace Notification.Api.Controllers;

public sealed class MarkNotificationsReadRequest
{
    public List<Guid>? NotificationIds { get; init; }

    public bool MarkAll { get; init; }
}
