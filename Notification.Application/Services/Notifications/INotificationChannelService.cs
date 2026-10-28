
using Notification.Domain.Enums;

namespace Notification.Application.Services.Notifications;

public interface INotificationChannelService
{
    // NOTI-07: Get channel configuration
    Task<NotificationChannelDto?> GetAsync(
        ChannelType code,
        CancellationToken cancellationToken = default
    );

    // NOTI-07: Update channel configuration
    Task<NotificationChannelDto?> UpdateAsync(
        ChannelType code,
        UpdateNotificationChannelDto request,
        CancellationToken cancellationToken = default
    );
}

// Response DTO
public sealed record NotificationChannelDto(
    Guid Id,
    ChannelType Code,
    string Name,
    ChannelProvider? Provider,
    string? SafeConfigJson,
    bool IsEnabled,
    DateTimeOffset UpdatedAt
);

// Update DTO
public sealed record UpdateNotificationChannelDto(
    string Name,
    ChannelProvider? Provider,
    string? ConfigJson,
    bool IsEnabled
);
