namespace Notification.Api.Controllers.Public;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Filters;
using Notification.Api.Extensions;
using Notification.Application.Services.OrderCompletions;
[ApiController]
[Route("api/v1/notifications/order-completions")]
[ResponseCache(NoStore = true,Location = ResponseCacheLocation.None)]
public sealed class OrderCompletionsController(IOrderCompletionService service) : ControllerBase
{
    /// <summary>Accepts buyer and seller notifications after Commerce commits order completion.</summary>
    [HttpPost]
    [RequireRole("service","admin")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(OrderCompletionResponse),StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateOrderCompletionRequest request,CancellationToken ct) => Ok(await service.CreateAsync(request,ct));
    /// <summary>Retrieves an owned logical order notification for a push or inbox action.</summary>
    [HttpGet("{notificationId:guid}")]
    [RequireRole]
    [ProducesResponseType(typeof(Notification.Application.Services.Notifications.InAppNotificationItemResponse),StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid notificationId,CancellationToken ct)
    {
        if (!this.TryGetUserId(out var userId,out var error)) return error;
        return Ok(await service.GetNotificationAsync(userId,notificationId,ct));
    }
}
