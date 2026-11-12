namespace Notification.Api.Controllers.Public;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Filters;
using Notification.Api.Extensions;
using Notification.Application.Services.OrderDecisions;
[ApiController]
[Route("api/v1/notifications/order-decisions")]
[ResponseCache(NoStore = true,Location = ResponseCacheLocation.None)]
public sealed class OrderDecisionsController(IOrderDecisionService service) : ControllerBase
{
    /// <summary>Accepts buyer and seller notifications after a seller decision and its order state commit.</summary>
    [HttpPost]
    [RequireRole("service","admin")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(OrderDecisionResponse),StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateOrderDecisionRequest request,CancellationToken ct) => Ok(await service.CreateAsync(request,ct));
    /// <summary>Retrieves an owned logical order notification for a push or inbox action.</summary>
    [HttpGet("{notificationId:guid}")]
    [RequireRole]
    public async Task<IActionResult> Get(Guid notificationId,CancellationToken ct)
    {
        if (!this.TryGetUserId(out var userId,out var error)) return error;
        return Ok(await service.GetNotificationAsync(userId,notificationId,ct));
    }
}
