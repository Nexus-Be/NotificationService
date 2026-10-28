
namespace Notification.Application.Services.Notifications;

public interface IUserNotificationService
{
    Task<NotificationPageDto> ListAsync(
        Guid userId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default
    );

    Task<NotificationDto?> MarkAsReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default
    );

    // Đánh dấu nhiều thông báo đã đọc
    Task<int> MarkManyAsReadAsync(
        Guid userId,
        IReadOnlyCollection<Guid> notificationIds,
        CancellationToken cancellationToken = default
    );

    Task<int> GetUnreadCountAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    );

    Task<int> MarkAllAsReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    );
}
