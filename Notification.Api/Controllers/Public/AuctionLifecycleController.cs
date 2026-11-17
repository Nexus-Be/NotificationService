namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Extensions;
using Notification.Application.Services.AuctionLifecycle;

[ApiController]
[Route("api/v1/notifications")]
[Authorize(Policy = AuctionNotificationAuth.Policy)]
[RequestSizeLimit(16384)]
public sealed class AuctionLifecycleController(IAuctionLifecycleService service) : ControllerBase
{
    /// <summary>Accepts a failed auction notification for the seller.</summary>
    [HttpPost("auction-failed")]
    public async Task<ActionResult<AuctionLifecycleResponse>> Failed(AuctionFailedRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(request, ct));

    /// <summary>Accepts a cancelled auction notification for the seller.</summary>
    [HttpPost("auction-cancelled")]
    public async Task<ActionResult<AuctionLifecycleResponse>> Cancelled(AuctionCancelledRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(request, ct));

    /// <summary>Accepts a replacement winner invitation for the invited bidder.</summary>
    [HttpPost("auction-winner-offers")]
    public async Task<ActionResult<AuctionLifecycleResponse>> WinnerOffer(AuctionWinnerOfferRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(request, ct));
}
