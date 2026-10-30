namespace Notification.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Notification.Api.Extensions;
using Notification.Application.Services.Notifications;

[ApiController]
[Route("api/v1")]
public sealed class NotificationsController : ControllerBase
{
    private readonly IUserNotificationService _notificationService;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        IUserNotificationService notificationService,
        ILogger<NotificationsController> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>Registers or refreshes device token.</summary>
    [HttpPost("notifications/devices")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] RegisterDeviceRequest request,
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

        try
        {
            var userId = GetCurrentUserId();
            var result = await _notificationService.RegisterDeviceAsync(userId, request, cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Device token registered successfully.",
                data = result
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                errorCode = "VAL_INVALID_INPUT",
                message = ex.Message
            });
        }
    }

    /// <summary>Revokes device token on logout.</summary>
    [HttpDelete("notifications/devices/{token}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeDevice(
        [FromRoute] string token,
        CancellationToken cancellationToken)
    {
        await _notificationService.RevokeDeviceAsync(token, cancellationToken);
        return NoContent();
    }

    /// <summary>Retrieves user notification preferences.</summary>
    [HttpGet("notifications/preferences")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyPreferences(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var preferences = await _notificationService.GetPreferencesAsync(userId, cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Notification preferences retrieved successfully.",
            data = preferences
        });
    }

    /// <summary>Updates user notification preferences.</summary>
    [HttpPut("notifications/preferences")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePreferences(
        [FromBody] UpdatePreferencesRequest request,
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
        var result = await _notificationService.UpdatePreferencesAsync(userId, request, cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Notification preferences updated successfully.",
            data = result
        });
    }

    /// <summary>Retrieves personal in-app notifications history.</summary>
    [HttpGet("notifications/me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyNotifications(
        [FromQuery] bool? isRead = null,
        [FromQuery] string? notificationType = null,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var request = new GetMyNotificationsRequest(isRead, notificationType, pageIndex, pageSize);
        var result = await _notificationService.GetMyNotificationsAsync(userId, request, cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Notifications retrieved successfully.",
            data = result
        });
    }

    /// <summary>Marks notifications as read.</summary>
    [HttpPatch("notifications/me/read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkNotificationsAsRead(
        [FromBody] BatchMarkReadRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await _notificationService.MarkNotificationsAsReadAsync(userId, request, cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Notifications marked as read successfully.",
            data = result
        });
    }

    /// <summary>Searches system notification history.</summary>
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
        var request = new SearchNotificationHistoryRequest(userId, notificationType, status, pageIndex, pageSize);
        var result = await _notificationService.SearchNotificationHistoryAsync(request, cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Notification history retrieved successfully.",
            data = result
        });
    }

    /// <summary>Retrieves message broker health status.</summary>
    [HttpGet("admin/health/broker")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBrokerHealth(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var records = await _notificationService.GetBrokerHealthAsync(limit, cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Broker health status retrieved successfully.",
            data = records
        });
    }

    private Guid GetCurrentUserId()
    {
        return this.GetUserId()
            ?? (Request.Headers.TryGetValue("x-user-id", out var headerVal) && Guid.TryParse(headerVal.FirstOrDefault(), out var uid)
                ? uid
                : Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }
}
