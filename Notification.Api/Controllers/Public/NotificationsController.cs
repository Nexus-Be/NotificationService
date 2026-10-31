namespace Notification.Api.Controllers.Public;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Notification.Api.Extensions;
using Notification.Api.Filters;
using Notification.Application.Services.Notifications;

[ApiController]
[Route("api/v1/notifications")]
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
    [HttpPost("devices")]
    [RequireRole]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] RegisterDeviceRequest request,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId, out var authError))
        {
            return authError;
        }

        var result = await _notificationService.RegisterDeviceAsync(userId, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>Revokes device token on logout.</summary>
    [HttpDelete("devices/{token}")]
    [RequireRole]
    public async Task<IActionResult> RevokeDevice(
        [FromRoute] string token,
        CancellationToken cancellationToken)
    {
        await _notificationService.RevokeDeviceAsync(token, cancellationToken);
        return NoContent();
    }

    /// <summary>Retrieves user notification preferences.</summary>
    [HttpGet("preferences")]
    [RequireRole]
    public async Task<IActionResult> GetMyPreferences(CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId, out var authError))
        {
            return authError;
        }

        var preferences = await _notificationService.GetPreferencesAsync(userId, cancellationToken);
        return Ok(preferences);
    }

    /// <summary>Updates user notification preferences.</summary>
    [HttpPut("preferences")]
    [RequireRole]
    public async Task<IActionResult> UpdatePreferences(
        [FromBody] UpdatePreferencesRequest request,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId, out var authError))
        {
            return authError;
        }

        var result = await _notificationService.UpdatePreferencesAsync(userId, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>Retrieves personal in-app notifications history.</summary>
    [HttpGet]
    [HttpGet("me")]
    [RequireRole]
    public async Task<IActionResult> GetMyNotifications(
        [FromQuery] bool? isRead = null,
        [FromQuery] string? notificationType = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!this.TryGetUserId(out var userId, out var authError))
        {
            return authError;
        }

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var request = new GetMyNotificationsRequest(isRead, notificationType, page, pageSize);
        var result = await _notificationService.GetMyNotificationsAsync(userId, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>Marks a single notification as read by ID.</summary>
    [HttpPatch("{id:guid}/read")]
    [HttpPut("{id:guid}/read")]
    [RequireRole]
    public async Task<IActionResult> MarkSingleNotificationAsRead(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId, out var authError))
        {
            return authError;
        }

        var result = await _notificationService.MarkAsReadAsync(userId, id, cancellationToken);
        if (result is null)
        {
            return NotFound(new
            {
                errorCode = "NOTIFICATION_NOT_FOUND",
                message = "Notification not found or does not belong to the user."
            });
        }

        return Ok(result);
    }

    /// <summary>Marks multiple or all notifications as read.</summary>
    [HttpPatch("me/read")]
    [HttpPut("me/read")]
    [RequireRole]
    public async Task<IActionResult> MarkNotificationsAsRead(
        [FromBody] BatchMarkReadRequest request,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId, out var authError))
        {
            return authError;
        }

        var result = await _notificationService.MarkNotificationsAsReadAsync(userId, request, cancellationToken);
        return Ok(result);
    }
}