
using Notification.Domain.Enums;

namespace Notification.Application.Services.Notifications;

public interface INotificationTemplateService
{
    // NOTI-08: Get notification templates
    Task<NotificationTemplatePageDto> GetAsync(
        ChannelType? channelCode,
        NotificationType? notificationType,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default
    );

    // NOTI-08: Create notification template
    Task<NotificationTemplateDto> CreateAsync(
        CreateNotificationTemplateDto request,
        CancellationToken cancellationToken = default
    );
}

// Template response
public sealed record NotificationTemplateDto(
    Guid Id,
    string Code,
    NotificationType NotificationType,
    ChannelType ChannelCode,
    string? SubjectTemplate,
    string? BodyTemplate,
    bool IsMandatory,
    string Locale,
    bool IsActive
);

// Pagination response
public sealed record NotificationTemplatePageDto(
    IReadOnlyList<NotificationTemplateDto> Items,
    int TotalCount,
    int PageIndex,
    int PageSize
);

// Create template request
public sealed record CreateNotificationTemplateDto(
    string Code,
    NotificationType NotificationType,
    ChannelType ChannelCode,
    string? SubjectTemplate,
    string? BodyTemplate,
    bool IsMandatory,
    string Locale,
    bool IsActive
);
