
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Application.Services.Notifications;

namespace Notification.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly IUserNotificationService _notificationService;

    public NotificationsController(
        IUserNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyNotifications(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        // Get authenticated user ID from JWT
        var userIdClaim =
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId)
            || userId == Guid.Empty)
        {
            return Unauthorized(new
            {
                message = "Invalid user identity."
            });
        }

        if (pageIndex < 1 || pageSize < 1 || pageSize > 100)
        {
            return BadRequest(new
            {
                message = "Invalid pagination parameters."
            });
        }

        var notifications =
            await _notificationService.ListAsync(
                userId,
                pageIndex,
                pageSize,
                cancellationToken);

        var unreadCount =
            await _notificationService.GetUnreadCountAsync(
                userId,
                cancellationToken);

        return Ok(new
        {
            success = true,
            statusCode = 200,
            message = "Notifications retrieved successfully.",
            data = new
            {
                notifications,
                unreadCount
            },
            traceId = HttpContext.TraceIdentifier,
            timestamp = DateTimeOffset.UtcNow
        });
    }


    [HttpPatch("me/read")]
    [Authorize(Roles = "Buyer")]
    public async Task<IActionResult> MarkNotificationsAsRead(
        [FromBody] MarkNotificationsReadRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Lấy User ID từ JWT
        var userIdClaim =
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId)
            || userId == Guid.Empty)
        {
            return Unauthorized(new
            {
                message = "Invalid user identity."
            });
        }

        // 2. Validate request
        if (request is null)
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_INPUT",
                detail: "Request body is required.");
        }

        var ids = request.NotificationIds;

        if ((request.MarkAll && ids is { Count: > 0 })
            || (!request.MarkAll &&
                (ids is null || ids.Count == 0
                || ids.Any(id => id == Guid.Empty))))
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_INPUT",
                detail: "Notification IDs list is invalid.");
        }

        // 3. Đánh dấu đã đọc
        int updatedCount;

        if (request.MarkAll)
        {
            updatedCount =
                await _notificationService.MarkAllAsReadAsync(
                    userId,
                    cancellationToken);
        }
        else
        {
            updatedCount =
                await _notificationService.MarkManyAsReadAsync(
                    userId,
                    ids!,
                    cancellationToken);
        }

        // 4. Đếm số thông báo chưa đọc
        var unreadCount =
            await _notificationService.GetUnreadCountAsync(
                userId,
                cancellationToken);

        // 5. Trả response chuẩn
        return Ok(new
        {
            success = true,
            statusCode = 200,
            data = new
            {
                updatedCount,
                unreadCount
            },
            traceId = HttpContext.TraceIdentifier,
            timestamp = DateTimeOffset.UtcNow
        });
    }

}
