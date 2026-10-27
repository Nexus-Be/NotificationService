namespace Notification.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;

[ApiController]
[Route("api/v1")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationDbContext _dbContext;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        INotificationDbContext dbContext,
        ILogger<NotificationsController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Đăng ký hoặc làm mới device token (NOTI-01).
    /// </summary>
    [HttpPost("notifications/devices")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] RegisterDeviceApiRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new
            {
                errorCode = "VAL_INVALID_INPUT",
                message = "Device token cannot be empty."
            });
        }

        if (!Enum.TryParse<DevicePlatform>(request.Platform, true, out var platform))
        {
            return BadRequest(new
            {
                errorCode = "VAL_INVALID_INPUT",
                message = "Platform must be WEB, IOS, or ANDROID."
            });
        }

        var userId = GetCurrentUserId();

        var existing = await _dbContext.DeviceTokens
            .FirstOrDefaultAsync(d => d.Token == request.Token, cancellationToken);

        if (existing != null)
        {
            existing.UserId = userId;
            existing.Platform = platform;
            existing.IsActive = true;
            existing.LastSeenAt = DateTimeOffset.UtcNow;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
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
                LastSeenAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _dbContext.DeviceTokens.Add(device);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Device token registered successfully.",
            data = new
            {
                platform = platform.ToString().ToUpperInvariant(),
                token = request.Token,
                registeredAt = DateTimeOffset.UtcNow
            }
        });
    }

    /// <summary>
    /// Thu hồi hoặc vô hiệu hóa device token (NOTI-02).
    /// </summary>
    [HttpDelete("notifications/devices")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RevokeDevice(
        [FromBody] RevokeDeviceApiRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new
            {
                errorCode = "VAL_INVALID_INPUT",
                message = "Device token is required."
            });
        }

        var existing = await _dbContext.DeviceTokens
            .FirstOrDefaultAsync(d => d.Token == request.Token, cancellationToken);

        if (existing != null)
        {
            existing.IsActive = false;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    /// <summary>
    /// Lấy danh sách cấu hình nhận thông báo của người dùng (NOTI-03).
    /// </summary>
    [HttpGet("notifications/preferences")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyPreferences(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        var preferences = await _dbContext.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new
            {
                id = p.Id,
                notificationType = p.NotificationType.ToString(),
                channelCode = p.ChannelCode.ToString(),
                isDisabled = p.IsDisabled
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Notification preferences retrieved successfully.",
            data = preferences
        });
    }

    /// <summary>
    /// Cập nhật tùy chọn nhận thông báo của người dùng (NOTI-04).
    /// </summary>
    [HttpPut("notifications/preferences")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePreferences(
        [FromBody] UpdatePreferencesApiRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.Preferences == null || request.Preferences.Count == 0)
        {
            return BadRequest(new
            {
                errorCode = "VAL_INVALID_INPUT",
                message = "At least one preference must be provided."
            });
        }

        var userId = GetCurrentUserId();
        var existingPrefs = await _dbContext.NotificationPreferences
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        var updatedCount = 0;

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
                current.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _dbContext.NotificationPreferences.Add(new NotificationPreference
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    NotificationType = notiType,
                    ChannelCode = channelType,
                    IsDisabled = item.IsDisabled,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            updatedCount++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Notification preferences updated successfully.",
            data = new
            {
                updatedCount
            }
        });
    }

    /// <summary>
    /// Lấy danh sách lịch sử thông báo in-app quả chuông cá nhân (NOTI-05).
    /// </summary>
    [HttpGet("notifications/me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyNotifications(
        [FromQuery] bool? isRead = null,
        [FromQuery] string? notificationType = null,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var userId = GetCurrentUserId();
        var query = _dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId);

        if (isRead.HasValue)
        {
            query = query.Where(n => n.IsRead == isRead.Value);
        }

        if (!string.IsNullOrWhiteSpace(notificationType) &&
            Enum.TryParse<NotificationType>(notificationType, true, out var parsedType))
        {
            query = query.Where(n => n.NotificationType == parsedType);
        }

        var totalItems = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new
            {
                id = n.Id,
                userId = n.UserId,
                title = n.Title,
                body = n.Body,
                payload = n.Payload,
                isRead = n.IsRead,
                readAt = n.ReadAt,
                notificationType = n.NotificationType != null ? n.NotificationType.ToString() : null,
                status = n.Status.ToString(),
                createdAt = n.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Notifications retrieved successfully.",
            data = new
            {
                pageIndex,
                pageSize,
                totalItems,
                totalPages = (int)Math.Ceiling(totalItems / (double)pageSize),
                items
            }
        });
    }

    /// <summary>
    /// Đánh dấu thông báo đã đọc qua batch body (NOTI-06 / NOTI-08).
    /// </summary>
    [HttpPatch("notifications/me/read")]
    [HttpPatch("notifications/read-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkNotificationsAsRead(
        [FromBody] MarkReadApiRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        var query = _dbContext.Notifications.Where(n => n.UserId == userId && !n.IsRead);

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

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Notifications marked as read successfully.",
            data = new
            {
                markedCount = notificationsToUpdate.Count
            }
        });
    }

    /// <summary>
    /// Admin tra cứu lịch sử thông báo hệ thống (NOTI-09 sheet).
    /// </summary>
    [HttpGet("admin/notifications")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchNotificationHistory(
        [FromQuery] Guid? userId = null,
        [FromQuery] string? notificationType = null,
        [FromQuery] string? status = null,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _dbContext.Notifications.AsNoTracking();

        if (userId.HasValue)
        {
            query = query.Where(n => n.UserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(notificationType) &&
            Enum.TryParse<NotificationType>(notificationType, true, out var parsedType))
        {
            query = query.Where(n => n.NotificationType == parsedType);
        }

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<NotificationStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(n => n.Status == parsedStatus);
        }

        var totalItems = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new
            {
                id = n.Id,
                userId = n.UserId,
                title = n.Title,
                body = n.Body,
                isRead = n.IsRead,
                notificationType = n.NotificationType != null ? n.NotificationType.ToString() : null,
                status = n.Status.ToString(),
                createdAt = n.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Notification history retrieved successfully.",
            data = new
            {
                pageIndex,
                pageSize,
                totalItems,
                totalPages = (int)Math.Ceiling(totalItems / (double)pageSize),
                items
            }
        });
    }

    /// <summary>
    /// Admin kiểm tra trạng thái sức khỏe Message Broker (NOTI-16).
    /// </summary>
    [HttpGet("admin/health/broker")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBrokerHealth(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit < 1 || limit > 100) limit = 50;

        var records = await _dbContext.BrokerHealthChecks
            .AsNoTracking()
            .OrderByDescending(b => b.CheckedAt)
            .Take(limit)
            .Select(b => new
            {
                id = b.Id,
                brokerName = b.BrokerName,
                status = b.Status.ToString(),
                latencyMs = b.LatencyMs,
                queueDepth = b.QueueDepth,
                detail = b.Detail,
                checkedAt = b.CheckedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Broker health status retrieved successfully.",
            data = records
        });
    }

    private Guid GetCurrentUserId()
    {
        if (Request.Headers.TryGetValue("x-user-id", out var headerVal) &&
            Guid.TryParse(headerVal.FirstOrDefault(), out var uid))
        {
            return uid;
        }

        // Fallback demo GUID if header not present in direct testing
        return Guid.Parse("11111111-1111-1111-1111-111111111111");
    }
}

public sealed record RegisterDeviceApiRequest
{
    public string Platform { get; init; } = "WEB";
    public string Token { get; init; } = string.Empty;
}

public sealed record RevokeDeviceApiRequest
{
    public string Token { get; init; } = string.Empty;
}

public sealed record UpdatePreferencesApiRequest
{
    public IReadOnlyList<PreferenceItemApiRequest> Preferences { get; init; } = Array.Empty<PreferenceItemApiRequest>();
}

public sealed record PreferenceItemApiRequest
{
    public string NotificationType { get; init; } = string.Empty;
    public string ChannelCode { get; init; } = string.Empty;
    public bool IsDisabled { get; init; }
}

public sealed record MarkReadApiRequest
{
    public IReadOnlyList<Guid>? NotificationIds { get; init; }
    public bool? MarkAll { get; init; }
}
