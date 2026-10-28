
using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Domain.Enums;

namespace Notification.Application.Services.Notifications;

public sealed class NotificationHistoryService
    : INotificationHistoryService
{
    private readonly INotificationDbContext _db;

    public NotificationHistoryService(
        INotificationDbContext db)
    {
        _db = db;
    }

    // NOTI-09: Get notification history
    public async Task<NotificationHistoryPageDto> GetAsync(
        Guid? userId,
        NotificationType? notificationType,
        NotificationStatus? status,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate filters
        if (userId == Guid.Empty ||
            (notificationType.HasValue &&
             !Enum.IsDefined(notificationType.Value)) ||
            (status.HasValue &&
             !Enum.IsDefined(status.Value)))
        {
            throw new ArgumentException(
                "Invalid notification history filters.");
        }

        if (pageIndex < 1 || pageSize < 1 || pageSize > 100)
        {
            throw new ArgumentException(
                "Invalid pagination parameters.");
        }

        var offset = ((long)pageIndex - 1) * pageSize;

        if (offset > int.MaxValue)
        {
            throw new ArgumentException(
                "Requested page exceeds supported range.");
        }

        // 2. Build query
        var query = _db.Notifications
            .AsNoTracking()
            .Where(n => !n.IsDeleted);

        if (userId.HasValue)
        {
            query = query.Where(
                n => n.UserId == userId.Value);
        }

        if (notificationType.HasValue)
        {
            query = query.Where(
                n => n.NotificationType == notificationType.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(
                n => n.Status == status.Value);
        }

        // 3. Count records
        var total = await query.CountAsync(
            cancellationToken);

        // 4. Get paginated notifications
        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(n => new
            {
                n.Id,
                n.UserId,
                n.NotificationType,
                n.Status,
                n.CreatedAt
            })
            .ToListAsync(cancellationToken);

        if (notifications.Count == 0)
        {
            return new NotificationHistoryPageDto(
                Array.Empty<NotificationHistoryDto>(),
                total,
                pageIndex,
                pageSize);
        }

        // 5. Load deliveries
        var notificationIds = notifications
            .Select(n => n.Id)
            .ToArray();

        var deliveries = await _db.NotificationDeliveries
            .AsNoTracking()
            .Where(d =>
                !d.IsDeleted &&
                notificationIds.Contains(d.NotificationId))
            .Select(d => new
            {
                d.Id,
                d.NotificationId,
                d.ChannelCode,
                d.Destination,
                d.Status,
                d.AttemptCount
            })
            .ToListAsync(cancellationToken);

        // 6. Group deliveries
        var deliveryLookup = deliveries
            .GroupBy(d => d.NotificationId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<NotificationDeliveryDto>)
                    g.OrderBy(d => d.Id)
                     .Select(d => new NotificationDeliveryDto(
                         d.Id,
                         d.ChannelCode,
                         MaskDestination(
                             d.ChannelCode,
                             d.Destination),
                         d.Status,
                         d.AttemptCount))
                     .ToList());

        // 7. Build response
        var items = notifications
            .Select(n => new NotificationHistoryDto(
                n.Id,
                n.UserId,
                n.NotificationType,
                n.Status,
                n.CreatedAt,
                deliveryLookup.TryGetValue(
                    n.Id, out var matched)
                    ? matched
                    : Array.Empty<NotificationDeliveryDto>()))
            .ToList();

        return new NotificationHistoryPageDto(
            items,
            total,
            pageIndex,
            pageSize);
    }

    // Mask sensitive destinations
    private static string MaskDestination(
        ChannelType channel,
        string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            return "***";

        destination = destination.Trim();

        switch (channel)
        {
            case ChannelType.Email:
            {
                var at = destination.IndexOf('@');

                if (at <= 0 || at == destination.Length - 1)
                    return "***";

                return $"{destination[0]}***{destination[at..]}";
            }

            case ChannelType.Sms:
            {
                var digits = new string(
                    destination.Where(char.IsDigit).ToArray());

                return digits.Length > 4
                    ? "***" + digits[^4..]
                    : "***";
            }

            case ChannelType.Push:
            case ChannelType.InApp:
            default:
                return "***";
        }
    }
}
