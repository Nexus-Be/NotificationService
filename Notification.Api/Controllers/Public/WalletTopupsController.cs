namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Extensions;
using Notification.Application.Services.WalletTopups;

[ApiController]
[Route("api/v1/notifications/wallet-topups")]
[Authorize(Policy = WalletNotificationAuth.Policy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WalletTopupsController(IWalletTopupService service) : ControllerBase
{
    /// <summary>Accepts a confirmed Stripe wallet top-up from Wallet after the wallet credit commits.</summary>
    [HttpPost]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(WalletTopupResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] WalletTopupRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(request, ct));
}
