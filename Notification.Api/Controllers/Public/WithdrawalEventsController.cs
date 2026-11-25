namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Filters;
using Notification.Application.Services.WithdrawalEvents;

[ApiController]
[Route("api/v1/notifications/withdrawal-events")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WithdrawalEventsController(IWithdrawalEventService service) : ControllerBase
{
    /// <summary>Accepts the confirmed result of a withdrawal request (approved, rejected, completed or failed) from Wallet.</summary>
    [HttpPost]
    [RequireServiceKeyOrRole("service", "admin")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(WithdrawalEventResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] WithdrawalEventRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(request, ct));
}
