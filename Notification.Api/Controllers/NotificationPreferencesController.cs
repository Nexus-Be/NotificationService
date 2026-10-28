
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Application.Services.Notifications;
using Notification.Domain.Enums;

namespace Notification.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/notifications/preferences")]
public sealed class NotificationPreferencesController : ControllerBase
{
    private readonly INotificationPreferenceService _service;

    public NotificationPreferencesController(
        INotificationPreferenceService service)
    {
        _service = service;
    }

    // NOTI-03: Get notification preferences
    [HttpGet]
    public async Task<IActionResult> GetPreferences(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Get User ID from JWT
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

        // Validate pagination
        if (pageIndex < 1 || pageSize < 1 || pageSize > 100)
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_PAGE",
                detail: "Invalid pagination parameters.");
        }

        // Get data from Application Service
        var result = await _service.GetAsync(
            userId,
            pageIndex,
            pageSize,
            cancellationToken);

        var totalPages = result.TotalCount == 0
            ? 0
            : (result.TotalCount - 1) / pageSize + 1;

        return Ok(new
        {
            success = true,
            statusCode = 200,
            data = new
            {
                items = result.Items.Select(p => new
                {
                    notificationType = ToApiCode(p.NotificationType),
                    channelCode = ToApiCode(p.ChannelCode),
                    p.IsEnabled,
                    p.IsMandatory
                }),
                pagination = new
                {
                    pageIndex,
                    pageSize,
                    result.TotalCount,
                    totalPages,
                    hasPreviousPage = pageIndex > 1,
                    hasNextPage = pageIndex < totalPages
                }
            },
            traceId = HttpContext.TraceIdentifier,
            timestamp = DateTimeOffset.UtcNow
        });
    }


    // NOTI-04: Update notification preferences
    [HttpPut]
    public async Task<IActionResult> UpdatePreferences(
        [FromBody] UpdateNotificationPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Get authenticated user
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
        if (request?.Preferences is not
            { Count: > 0 and <= 100 } preferences)
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_PREFERENCES",
                detail: "Preferences list is invalid.");
        }

        var updates = new List<UpdatePreferenceDto>();

        foreach (var item in preferences)
        {
            if (item is null ||
                string.IsNullOrWhiteSpace(item.NotificationType) ||
                string.IsNullOrWhiteSpace(item.ChannelCode))
            {
                return Problem(
                    statusCode: 400,
                    title: "VAL_INVALID_PREFERENCES",
                    detail: "Notification type or channel code is invalid.");
            }

            var typeCode = item.NotificationType.Trim();
            var channelCode = item.ChannelCode.Trim();

            if (!Enum.TryParse<NotificationType>(
                    typeCode.Replace("_", ""),
                    true,
                    out var notificationType)
                || !Enum.IsDefined(notificationType)
                || !string.Equals(
                    ToApiCode(notificationType),
                    typeCode,
                    StringComparison.OrdinalIgnoreCase)
                || !Enum.TryParse<ChannelType>(
                    channelCode.Replace("_", ""),
                    true,
                    out var channel)
                || !Enum.IsDefined(channel)
                || !string.Equals(
                    ToApiCode(channel),
                    channelCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Problem(
                    statusCode: 400,
                    title: "VAL_INVALID_PREFERENCES",
                    detail: "Notification type or channel code is invalid.");
            }

            updates.Add(new UpdatePreferenceDto(
                notificationType,
                channel,
                item.IsEnabled));
        }

        // 3. Update database
        NotificationPreferenceUpdateResult result;

        try
        {
            result = await _service.UpdateAsync(
                userId,
                updates,
                cancellationToken);
        }
        catch (ArgumentException)
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_PREFERENCES",
                detail: "Notification type or channel code is invalid.");
        }
        catch (InvalidOperationException ex)
            when (ex.Message ==
                  "MANDATORY_NOTIFICATION_CANNOT_DISABLE")
        {
            return Problem(
                statusCode: 422,
                title: "MANDATORY_NOTIFICATION_CANNOT_DISABLE",
                detail: "Mandatory security or transaction notification cannot be disabled.");
        }

        // 4. Return response
        return Ok(new
        {
            success = true,
            statusCode = 200,
            data = new
            {
                updatedCount = result.UpdatedCount,
                preferences = result.Preferences.Select(p => new
                {
                    notificationType = ToApiCode(p.NotificationType),
                    channelCode = ToApiCode(p.ChannelCode),
                    isEnabled = p.IsEnabled
                })
            },
            traceId = HttpContext.TraceIdentifier,
            timestamp = DateTimeOffset.UtcNow
        });
    }


    private static string ToApiCode<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return Regex.Replace(
            value.ToString(),
            "([a-z0-9])([A-Z])",
            "$1_$2").ToUpperInvariant();
    }
}
