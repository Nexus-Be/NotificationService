namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Mvc;
using Notification.Api.Filters;
using Notification.Application.Services.Notifications;

[ApiController]
[Route("api/v1/notifications/in-app")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InAppNotificationsController(IUserNotificationService service) : ControllerBase
{
    /// <summary>Creates a recipient notification from an authorized service or administrator.</summary>
    [HttpPost]
    [RequireRole("service", "admin", "staff")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(InAppNotificationItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Create(CreateInAppNotificationRequest request, CancellationToken ct)
    {
        var result = await service.CreateInAppNotificationAsync(request, ct);
        return result is null ? NoContent() : Ok(result);
    }
}
