namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Extensions;
using Notification.Api.Filters;
using Notification.Application.Services.OrderBuyerCancellations;
using Notification.Application.Services.Notifications;
[ApiController]
[Route("api/v1/notifications/order-buyer-cancellations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OrderBuyerCancellationsController(IOrderBuyerCancellationService service) : ControllerBase
{
    /// <summary>Accepts buyer and seller notices after a paid regular order is cancelled by its buyer.</summary>
    [HttpPost]
    [RequireRole("service", "admin")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(OrderBuyerCancellationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateOrderBuyerCancellationRequest request, CancellationToken ct) => Ok(await service.CreateAsync(request, ct));
    /// <summary>Retrieves an owned buyer cancellation notice without changing its read state.</summary>
    [HttpGet("{notificationId:guid}")]
    [RequireRole]
    [ProducesResponseType(typeof(InAppNotificationItemResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid notificationId, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var userId, out var error)) return error;
        return Ok(await service.GetNotificationAsync(userId, notificationId, ct));
    }
}
