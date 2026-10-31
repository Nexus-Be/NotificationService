namespace Notification.Api.Controllers.Public;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Notification.Api.Filters;
using Notification.Application.Services.Notifications;

[ApiController]
[Route("api/v1/admin/notifications")]
public sealed class AdminNotificationsController : ControllerBase
{
    private readonly IUserNotificationService _notificationService;
    private readonly ILogger<AdminNotificationsController> _logger;

    public AdminNotificationsController(
        IUserNotificationService notificationService,
        ILogger<AdminNotificationsController> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>Searches system notification history.</summary>
    [HttpGet]
    [RequireRole("admin", "staff")]
    public async Task<IActionResult> SearchNotificationHistory(
        [FromQuery] Guid? userId = null,
        [FromQuery] string? notificationType = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var request = new SearchNotificationHistoryRequest(userId, notificationType, status, page, pageSize);
        var result = await _notificationService.SearchNotificationHistoryAsync(request, cancellationToken);
        return Ok(result);
    }
}
