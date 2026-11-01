namespace Notification.Api.Controllers.Internal;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Notification.Application.Services.Notifications;

[ApiController]
[Route("api/v1/internal/notifications")]
public sealed class InternalNotificationsController : ControllerBase
{
    private readonly IUserNotificationService _notificationService;
    private readonly ILogger<InternalNotificationsController> _logger;

    public InternalNotificationsController(
        IUserNotificationService notificationService,
        ILogger<InternalNotificationsController> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>Creates an in-app notification for a user from internal services (Order, Auction, Fulfillment, User).</summary>
    [HttpPost("in-app")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateInAppNotification(
        [FromBody] CreateInAppNotificationRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || request.UserId == Guid.Empty)
        {
            return BadRequest(new
            {
                errorCode = "VAL_INVALID_INPUT",
                message = "User ID is required and must not be empty."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new
            {
                errorCode = "VAL_INVALID_INPUT",
                message = "Notification title is required."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new
            {
                errorCode = "VAL_INVALID_INPUT",
                message = "Notification body is required."
            });
        }

        _logger.LogInformation(
            "[Internal Notification] Creating in-app notification for user '{UserId}' with type '{NotificationType}'.",
            request.UserId,
            request.NotificationType ?? "Unspecified");

        var created = await _notificationService.CreateInAppNotificationAsync(request, cancellationToken);

        if (created is null)
        {
            return Ok(new
            {
                success = true,
                skipped = true,
                message = "Notification skipped because the user disabled in-app notifications for this category."
            });
        }

        return Ok(new
        {
            success = true,
            skipped = false,
            message = "In-app notification created successfully.",
            data = created
        });
    }
}