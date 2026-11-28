namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Extensions;
using Notification.Api.Filters;
using Notification.Application.Services.AuctionOutbids;
using Notification.Application.Services.Notifications;
using Microsoft.AspNetCore.SignalR;
using Notification.Api.Hubs;

[ApiController]
[Route("api/v1/notifications/auction-outbids")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuctionOutbidsController(IAuctionOutbidService service,
    IHubContext<NotificationHub> hub, ILogger<AuctionOutbidsController> logger) : ControllerBase
{
    /// <summary>Accepts a notification for the previous highest bidder after a higher bid commits.</summary>
    [HttpPost]
    [Authorize(Policy = AuctionNotificationAuth.Policy)]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(AuctionOutbidResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateAuctionOutbidRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        if (!result.IsReplay && result.InboxVisible)
        {
            try
            {
                await hub.Clients.User(result.UserId.ToString()).SendAsync("NotificationCreated", new
                {
                    notificationId = result.NotificationId,
                    type = "AuctionOutbid",
                    createdAt = DateTimeOffset.UtcNow
                }, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Realtime notification failed after outbid {NotificationId} was stored.",
                    result.NotificationId);
            }
        }
        return Ok(result);
    }
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
