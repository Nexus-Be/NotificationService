namespace Notification.Application.Services.Notifications;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Common;
using Notification.Application.Common.Extensions;
using Notification.Application.Common.Models;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;

public sealed partial class UserNotificationService : IUserNotificationService
{
    private readonly INotificationDbContext _db;
    private readonly TimeProvider _time;

    public UserNotificationService(INotificationDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    public async Task<InAppNotificationItemResponse?> CreateInAppNotificationAsync(
        CreateInAppNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentException("Notification request cannot be null.");

        ValidateUserId(request.UserId);

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Notification title is required.");

        if (string.IsNullOrWhiteSpace(request.Body))
            throw new ArgumentException("Notification body is required.");

        NotificationType? parsedType = string.IsNullOrWhiteSpace(request.NotificationType) ? null :
            NotificationPreferencePolicy.Parse<NotificationType>(request.NotificationType);

        if (request.SourceEventId.HasValue && request.SourceEventId.Value != Guid.Empty)
        {
            var existing = await _db.Notifications
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    n => n.SourceEventId == request.SourceEventId.Value
                        && n.UserId == request.UserId
                        && !n.IsDeleted && n.IsInboxVisible,
                    cancellationToken);

            if (existing is not null)
            {
                return new InAppNotificationItemResponse(
                    existing.Id,
                    existing.UserId,
                    existing.Title,
                    existing.Body,
                    existing.Payload,
                    existing.IsRead,
                    existing.ReadAt,
                    existing.NotificationType?.ToString(),
                    existing.Status.ToString(),
                    existing.CreatedAt
                );
            }
        }

        var channel = await _db.NotificationChannels.AsNoTracking().SingleOrDefaultAsync(c => c.Code == ChannelType.InApp && !c.IsDeleted, cancellationToken);
        if (channel is null) throw new Notification.Domain.Exceptions.DomainException("CHANNEL_NOT_FOUND", "Notification channel was not found.", 404);
        if (channel.IsDisabled) return null;
        if (parsedType.HasValue)
        {
            var mandatory = NotificationPreferencePolicy.IsCritical(parsedType.Value) ||
                (await NotificationPreferencePolicy.MandatoryPairsAsync(_db, cancellationToken)).Contains((parsedType.Value, ChannelType.InApp));
            if (!mandatory && await _db.NotificationPreferences.AsNoTracking().AnyAsync(p => p.UserId == request.UserId &&
                p.NotificationType == parsedType.Value && p.ChannelCode == ChannelType.InApp && p.IsDisabled && !p.IsDeleted, cancellationToken)) return null;
        }

        var now = DateTimeOffset.UtcNow;
        var notificationId = Guid.NewGuid();

        var record = new NotificationRecord
        {
            Id = notificationId,
            UserId = request.UserId,
            SourceEventId = request.SourceEventId,
            NotificationType = parsedType,
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            Payload = string.IsNullOrWhiteSpace(request.Payload) ? null : request.Payload.Trim(),
            IsRead = false,
            ReadAt = null,
            Status = NotificationStatus.Completed,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };

        var delivery = new NotificationDelivery
        {
            Id = Guid.NewGuid(),
            NotificationId = notificationId,
            ChannelCode = ChannelType.InApp,
            Destination = request.UserId.ToString(),
            Status = DeliveryStatus.Sent,
            AttemptCount = 1,
            LastAttemptAt = now,
            RowVersion = 1,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };

        _db.Notifications.Add(record);
        _db.NotificationDeliveries.Add(delivery);
        await _db.SaveChangesAsync(cancellationToken);

        return new InAppNotificationItemResponse(
            record.Id,
            record.UserId,
            record.Title,
            record.Body,
            record.Payload,
            record.IsRead,
            record.ReadAt,
            record.NotificationType?.ToString(),
            record.Status.ToString(),
            record.CreatedAt
        );
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
                    && !n.IsDeleted && n.IsInboxVisible,
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

    public async Task<PagedData<InAppNotificationItemResponse>> GetMyNotificationsAsync(
        Guid userId,
        GetMyNotificationsRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var (pageIndex, pageSize) = PaginationConstants.Normalize(request.PageIndex, request.PageSize);

        var query = _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsDeleted && n.IsInboxVisible);

        if (request.IsRead.HasValue)
        {
            query = query.Where(n => n.IsRead == request.IsRead.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.NotificationType) &&
            Enum.TryParse<NotificationType>(request.NotificationType, true, out var parsedType))
        {
            query = query.Where(n => n.NotificationType == parsedType);
        }

        var totalItems = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new InAppNotificationItemResponse(
                n.Id,
                n.UserId,
                n.Title,
                n.Body,
                n.Payload,
                n.IsRead,
                n.ReadAt,
                n.NotificationType != null ? n.NotificationType.ToString() : null,
                n.Status.ToString(),
                n.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return new PagedData<InAppNotificationItemResponse>(
            items,
            PaginationMetadata.Create(pageIndex, pageSize, totalItems)
        );
    }

    public async Task<BatchMarkReadResult> MarkNotificationsAsReadAsync(
        Guid userId,
        BatchMarkReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var query = _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead && !n.IsDeleted && n.IsInboxVisible);

        if (request?.NotificationIds != null && request.NotificationIds.Count > 0)
        {
            query = query.Where(n => request.NotificationIds.Contains(n.Id));
        }

        var notificationsToUpdate = await query.ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var noti in notificationsToUpdate)
        {
            noti.IsRead = true;
            noti.ReadAt = now;
            noti.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new BatchMarkReadResult(notificationsToUpdate.Count);
    }

    public async Task<PagedData<InAppNotificationItemResponse>> SearchNotificationHistoryAsync(
        SearchNotificationHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var (pageIndex, pageSize) = PaginationConstants.Normalize(request.PageIndex, request.PageSize);

        var query = _db.Notifications
            .AsNoTracking()
            .Where(n => !n.IsDeleted);

        if (request.UserId.HasValue)
        {
            query = query.Where(n => n.UserId == request.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.NotificationType) &&
            Enum.TryParse<NotificationType>(request.NotificationType, true, out var parsedType))
        {
            query = query.Where(n => n.NotificationType == parsedType);
        }

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<NotificationStatus>(request.Status, true, out var parsedStatus))
        {
            query = query.Where(n => n.Status == parsedStatus);
        }

        var totalItems = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new InAppNotificationItemResponse(
                n.Id,
                n.UserId,
                n.Title,
                n.Body,
                n.Payload,
                n.IsRead,
                n.ReadAt,
                n.NotificationType != null ? n.NotificationType.ToString() : null,
                n.Status.ToString(),
                n.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return new PagedData<InAppNotificationItemResponse>(
            items,
            PaginationMetadata.Create(pageIndex, pageSize, totalItems)
        );
    }

    public async Task<IReadOnlyList<BrokerHealthCheckResponse>> GetBrokerHealthAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit < 1 || limit > 100) limit = 50;

        var records = await _db.BrokerHealthChecks
            .AsNoTracking()
            .OrderByDescending(b => b.CheckedAt)
            .Take(limit)
            .Select(b => new BrokerHealthCheckResponse(
                b.Id,
                b.BrokerName,
                b.Status.ToString(),
                b.LatencyMs.HasValue ? (long)b.LatencyMs.Value : 0L,
                b.QueueDepth.HasValue ? b.QueueDepth.Value : 0,
                b.Detail,
                b.CheckedAt
            ))
            .ToListAsync(cancellationToken);

        return records;
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID must not be empty.");
    }
}