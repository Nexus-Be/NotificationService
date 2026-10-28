
namespace Notification.Api.Controllers;

// NOTI-04: Update notification preferences
public sealed class UpdateNotificationPreferencesRequest
{
    public List<NotificationPreferenceItemRequest>?
        Preferences
    { get; init; }
}

public sealed class NotificationPreferenceItemRequest
{
    public string? NotificationType { get; init; }

    public string? ChannelCode { get; init; }

    public bool IsEnabled { get; init; }
}
