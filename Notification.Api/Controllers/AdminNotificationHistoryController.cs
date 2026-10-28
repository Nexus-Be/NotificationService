
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Application.Services.Notifications;
using Notification.Domain.Enums;

namespace Notification.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/v1/admin/notifications")]
public sealed class AdminNotificationHistoryController
    : ControllerBase
{
    private readonly INotificationHistoryService _service;

    public AdminNotificationHistoryController(
        INotificationHistoryService service)
    {
        _service = service;
    }

    // NOTI-09: Get notification history
    [HttpGet]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string? userId,
        [FromQuery] string? notificationType,
        [FromQuery] string? status,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate pagination
        if (pageIndex < 1 ||
            pageSize < 1 ||
            pageSize > 100 ||
            ((long)pageIndex - 1) * pageSize > int.MaxValue)
        {
            return InvalidFilter();
        }

        // 2. Parse User ID
        Guid? parsedUserId = null;

        if (userId is not null)
        {
            if (!Guid.TryParse(userId, out var id) ||
                id == Guid.Empty)
            {
                return InvalidFilter();
            }

            parsedUserId = id;
        }

        // 3. Parse Notification Type
        NotificationType? parsedType = null;

        if (notificationType is not null)
        {
            if (!TryParseApiCode(
                notificationType,
                out NotificationType type))
            {
                return InvalidFilter();
            }

            parsedType = type;
        }

        // 4. Parse Notification Status
        NotificationStatus? parsedStatus = null;

        if (status is not null)
        {
            if (!TryParseApiCode(
                status,
                out NotificationStatus value))
            {
                return InvalidFilter();
            }

            parsedStatus = value;
        }

        // 5. Query Application Service
        var result = await _service.GetAsync(
            parsedUserId,
            parsedType,
            parsedStatus,
            pageIndex,
            pageSize,
            cancellationToken);

        // 6. Calculate pagination
        var totalPages = result.TotalCount == 0
            ? 0
            : (result.TotalCount - 1) / pageSize + 1;

        // 7. Return response
        return Ok(new
        {
            success = true,
            statusCode = 200,
            data = new
            {
                items = result.Items.Select(n => new
                {
                    notificationId = n.NotificationId,
                    userId = n.UserId,
                    notificationType =
                        n.NotificationType.HasValue
                            ? ToApiCode(n.NotificationType.Value)
                            : null,
                    status = ToApiCode(n.Status),
                    createdAt = n.CreatedAt,

                    deliveries = n.Deliveries.Select(d => new
                    {
                        deliveryId = d.DeliveryId,
                        channelCode = ToApiCode(d.ChannelCode),
                        destination = d.MaskedDestination,
                        status = ToApiCode(d.Status),
                        attemptCount = d.AttemptCount
                    })
                }),

                pagination = new
                {
                    pageIndex,
                    pageSize,
                    totalCount = result.TotalCount,
                    totalPages,
                    hasPreviousPage = pageIndex > 1,
                    hasNextPage = pageIndex < totalPages
                }
            },
            traceId = HttpContext.TraceIdentifier,
            timestamp = DateTimeOffset.UtcNow
        });
    }

    private IActionResult InvalidFilter()
    {
        return Problem(
            statusCode: 400,
            title: "VAL_INVALID_FILTER",
            detail: "Query filter parameters are invalid.");
    }

    // Parse API code into enum
    private static bool TryParseApiCode<TEnum>(
        string code,
        out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;

        if (string.IsNullOrWhiteSpace(code))
            return false;

        var normalized = code.Trim().Replace("_", "");

        return Enum.TryParse(
                   normalized, true, out value)
               && Enum.IsDefined(value)
               && string.Equals(
                   ToApiCode(value),
                   code.Trim(),
                   StringComparison.OrdinalIgnoreCase);
    }

    // Convert enum to API format
    private static string ToApiCode<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return Regex.Replace(
            value.ToString(),
            "([a-z0-9])([A-Z])",
            "$1_$2").ToUpperInvariant();
    }
}
