
using Notification.Domain.Enums;

namespace Notification.Application.Services.Notifications;

public interface INotificationPreferenceService
{
    // NOTI-03: Get notification preferences
    Task<NotificationPreferencePageDto> GetAsync(
        Guid userId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default
    );

    // NOTI-04: Update notification preferences
    Task<NotificationPreferenceUpdateResult> UpdateAsync(
        Guid userId,
        IReadOnlyCollection<UpdatePreferenceDto> preferences,
        CancellationToken cancellationToken = default
    );
}

// Preference response
public sealed record NotificationPreferenceDto(
    NotificationType NotificationType,
    ChannelType ChannelCode,
    bool IsEnabled,
    bool IsMandatory
);

// Pagination response
public sealed record NotificationPreferencePageDto(
    IReadOnlyList<NotificationPreferenceDto> Items,
    int TotalCount,
    int PageIndex,
    int PageSize
);

// Update request data
public sealed record UpdatePreferenceDto(
    NotificationType NotificationType,
    ChannelType ChannelCode,
    bool IsEnabled
);

// Update result
public sealed record NotificationPreferenceUpdateResult(
    int UpdatedCount,
    IReadOnlyList<NotificationPreferenceDto> Preferences
);
