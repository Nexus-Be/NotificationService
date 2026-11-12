namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Extensions;
using Notification.Api.Filters;
using Notification.Application.Services.AuctionOutbids;
using Notification.Application.Services.Notifications;

[ApiController]
[Route("api/v1/notifications/auction-outbids")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuctionOutbidsController(IAuctionOutbidService service) : ControllerBase
{
    /// <summary>Accepts a notification for the previous highest bidder after a higher bid commits.</summary>
    [HttpPost]
    [RequireRole("service", "admin")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(AuctionOutbidResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateAuctionOutbidRequest request, CancellationToken ct) => Ok(await service.CreateAsync(request, ct));
    /// <summary>Retrieves an owned outbid notification for an inbox or push action.</summary>
    [HttpGet("{notificationId:guid}")]
    [RequireRole]
    [ProducesResponseType(typeof(InAppNotificationItemResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid notificationId, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var userId, out var error)) return error;
        return Ok(await service.GetNotificationAsync(userId, notificationId, ct));
    }
}
