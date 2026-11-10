namespace Notification.Application.Services.Notifications;

using Microsoft.EntityFrameworkCore;
using Notification.Domain.Exceptions;

public sealed partial class UserNotificationService
{
    public async Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        var row = await _db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId && !n.IsDeleted, cancellationToken);
        if (row is null) throw new DomainException("NOTIFICATION_NOT_FOUND", "Notification was not found or does not belong to the user.", 404);
        return new(row.Id, row.UserId, row.Title, row.Body, row.Payload, row.IsRead, row.ReadAt, row.NotificationType?.ToString(), row.Status.ToString(), row.CreatedAt);
    }
}
