namespace Notification.Application.Services.Notifications;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;

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

    public async Task<DeviceRegisteredResponse> RegisterDeviceAsync(
        Guid userId,
        RegisterDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        if (string.IsNullOrWhiteSpace(request.Token))
            throw new ArgumentException("Device token cannot be empty.");

        if (!Enum.TryParse<DevicePlatform>(request.Platform, true, out var platform))
            throw new ArgumentException("Platform must be WEB, IOS, or ANDROID.");

        var existing = await _db.DeviceTokens
            .FirstOrDefaultAsync(d => d.Token == request.Token, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (existing != null)
        {
            existing.UserId = userId;
            existing.Platform = platform;
            existing.IsActive = true;
            existing.LastSeenAt = now;
            existing.UpdatedAt = now;
        }
        else
        {
            var device = new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Platform = platform,
                Token = request.Token,
                IsActive = true,
                LastSeenAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.DeviceTokens.Add(device);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new DeviceRegisteredResponse(
            platform.ToString().ToUpperInvariant(),
            request.Token,
            now
        );
    }

    public async Task RevokeDeviceAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        var existing = await _db.DeviceTokens
            .FirstOrDefaultAsync(d => d.Token == token, cancellationToken);

        if (existing != null)
        {
            existing.IsActive = false;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<NotificationPreferenceResponse>> GetPreferencesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var preferences = await _db.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new NotificationPreferenceResponse(
                p.Id,
                p.NotificationType.ToString(),
                p.ChannelCode.ToString(),
                p.IsDisabled
            ))
            .ToListAsync(cancellationToken);

        return preferences;
    }

    public async Task<UpdatePreferencesResult> UpdatePreferencesAsync(
        Guid userId,
        UpdatePreferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        if (request?.Preferences == null || request.Preferences.Count == 0)
            throw new ArgumentException("At least one preference must be provided.");

        var existingPrefs = await _db.NotificationPreferences
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        var updatedCount = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var item in request.Preferences)
        {
            if (!Enum.TryParse<NotificationType>(item.NotificationType, true, out var notiType) ||
                !Enum.TryParse<ChannelType>(item.ChannelCode, true, out var channelType))
            {
                continue;
            }

            var current = existingPrefs.FirstOrDefault(p =>
                p.NotificationType == notiType && p.ChannelCode == channelType);

            if (current != null)
            {
                current.IsDisabled = item.IsDisabled;
                current.UpdatedAt = now;
            }
            else
            {
                _db.NotificationPreferences.Add(new NotificationPreference
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    NotificationType = notiType,
                    ChannelCode = channelType,
                    IsDisabled = item.IsDisabled,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            updatedCount++;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new UpdatePreferencesResult(updatedCount);
    }

    public async Task<PagedInAppNotificationResponse> GetMyNotificationsAsync(
        Guid userId,
        GetMyNotificationsRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = request.PageSize < 1 || request.PageSize > 100 ? 20 : request.PageSize;

        var query = _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsDeleted);

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

        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        return new PagedInAppNotificationResponse(
            pageIndex,
            pageSize,
            totalItems,
            totalPages,
            items
        );
    }

    public async Task<BatchMarkReadResult> MarkNotificationsAsReadAsync(
        Guid userId,
        BatchMarkReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var query = _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead && !n.IsDeleted);

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

    public async Task<PagedInAppNotificationResponse> SearchNotificationHistoryAsync(
        SearchNotificationHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = request.PageSize < 1 || request.PageSize > 100 ? 20 : request.PageSize;

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
                null,
                n.NotificationType != null ? n.NotificationType.ToString() : null,
                n.Status.ToString(),
                n.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        return new PagedInAppNotificationResponse(
            pageIndex,
            pageSize,
            totalItems,
            totalPages,
            items
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
