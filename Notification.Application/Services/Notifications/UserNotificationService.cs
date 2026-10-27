namespace Notification.Application.Services.Notifications;

using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;

public sealed class UserNotificationService : IUserNotificationService
{
    private readonly INotificationDbContext _db;

    public UserNotificationService(INotificationDbContext db)
    {
        _db = db;
    }

    public async Task<NotificationPageDto> ListAsync(
        Guid userId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        if (pageIndex < 1)
            throw new ArgumentException("Page index must be at least 1.");

        if (pageSize < 1 || pageSize > 100)
            throw new ArgumentException("Page size must be between 1 and 100.");

        var offset = ((long)pageIndex - 1) * pageSize;
        if (offset > int.MaxValue)
            throw new ArgumentException("Requested page exceeds the supported range.");

        var query = _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsDeleted);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(n => new NotificationDto(
                n.Id,
                n.Title,
                n.Body,
                n.IsRead,
                n.CreatedAt,
                n.ReadAt))
            .ToListAsync(cancellationToken);

        return new NotificationPageDto(items, total, pageIndex, pageSize);
    }

    public async Task<NotificationDto?> MarkAsReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        if (notificationId == Guid.Empty)
            throw new ArgumentException("Notification ID must not be empty.");

        var notification = await _db.Notifications
            .AsTracking()
            .SingleOrDefaultAsync(
                n => n.Id == notificationId
                    && n.UserId == userId
                    && !n.IsDeleted,
                cancellationToken);

        if (notification is null)
            return null;

        if (!notification.IsRead)
        {
            notification.MarkAsRead();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new NotificationDto(
            notification.Id,
            notification.Title,
            notification.Body,
            notification.IsRead,
            notification.CreatedAt,
            notification.ReadAt);
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID must not be empty.");
    }
}