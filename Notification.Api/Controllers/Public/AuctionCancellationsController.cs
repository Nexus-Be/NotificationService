namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Extensions;
using Notification.Api.Filters;
using Notification.Application.Services.AuctionCancellations;
using Notification.Application.Services.Notifications;
[ApiController]
[Route("api/v1/notifications/auction-cancellations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuctionCancellationsController(IAuctionCancellationService service) : ControllerBase
{
    /// <summary>Accepts a cancellation notification for one verified affected recipient after source commit.</summary>
    [HttpPost]
    [RequireRole("service", "admin")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(AuctionCancellationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateAuctionCancellationRequest request, CancellationToken ct) => Ok(await service.CreateAsync(request, ct));
    /// <summary>Retrieves an owned cancellation notification without changing its read state.</summary>
    [HttpGet("{notificationId:guid}")]
    [RequireRole]
    [ProducesResponseType(typeof(InAppNotificationItemResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid notificationId, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var userId, out var error)) return error;
        return Ok(await service.GetNotificationAsync(userId, notificationId, ct));
    }
}
