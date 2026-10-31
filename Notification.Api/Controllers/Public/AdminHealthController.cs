namespace Notification.Api.Controllers.Public;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Notification.Api.Filters;
using Notification.Application.Services.Notifications;

[ApiController]
[Route("api/v1/admin/health")]
public sealed class AdminHealthController : ControllerBase
{
    private readonly IUserNotificationService _notificationService;
    private readonly ILogger<AdminHealthController> _logger;

    public AdminHealthController(
        IUserNotificationService notificationService,
        ILogger<AdminHealthController> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>Retrieves message broker health status.</summary>
    [HttpGet("broker")]
    [RequireRole("admin", "staff")]
    public async Task<IActionResult> GetBrokerHealth(CancellationToken cancellationToken)
    {
        var result = await _notificationService.GetBrokerHealthAsync(50, cancellationToken);
        return Ok(result);
    }
}
