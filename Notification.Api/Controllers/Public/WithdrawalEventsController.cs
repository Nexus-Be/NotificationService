namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Extensions;
using Notification.Application.Services.ServiceEvents;
using Notification.Application.Services.WalletEvents;

[ApiController]
[Route("api/v1/notifications/withdrawal-events")]
[Authorize(Policy = WalletNotificationAuth.Policy)]
[RequestSizeLimit(16384)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WithdrawalEventsController(IWalletEventService service) : ControllerBase
{
    /// <summary>Accepts a committed withdrawal status from the authenticated Wallet caller.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ServiceEventResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(WithdrawalEventRequest request, CancellationToken ct) =>
        Ok(await service.CreateWithdrawalAsync(request, ct));
}
