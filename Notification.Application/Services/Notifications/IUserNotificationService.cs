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
}