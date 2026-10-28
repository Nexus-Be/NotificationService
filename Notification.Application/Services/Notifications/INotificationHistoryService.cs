
using Notification.Domain.Enums;

namespace Notification.Application.Services.Notifications;

public interface INotificationHistoryService
{
    // NOTI-09: Get notification delivery history
    Task<NotificationHistoryPageDto> GetAsync(
        Guid? userId,
        NotificationType? notificationType,
        NotificationStatus? status,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default
    );
}

// Notification history response
public sealed record NotificationHistoryDto(
    Guid NotificationId,
    Guid UserId,
    NotificationType? NotificationType,
    NotificationStatus Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<NotificationDeliveryDto> Deliveries
);

// Delivery information
public sealed record NotificationDeliveryDto(
    Guid DeliveryId,
    ChannelType ChannelCode,
    string MaskedDestination,
    DeliveryStatus Status,
    int AttemptCount
);

// Paginated response
public sealed record NotificationHistoryPageDto(
    IReadOnlyList<NotificationHistoryDto> Items,
    int TotalCount,
    int PageIndex,
    int PageSize
);
