namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Extensions;
using Notification.Application.Services.AuctionActivity;

[ApiController]
[Route("api/v1/notifications")]
[Authorize(Policy = AuctionNotificationAuth.Policy)]
[RequestSizeLimit(131072)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuctionActivityController(IAuctionActivityService service) : ControllerBase
{
    [HttpPost("auction-reserve-approval-needed")]
    public async Task<IActionResult> ReserveApproval(AuctionReserveApprovalNeededRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(request, ct));

    [HttpPost("auction-reserve-restriction")]
    public async Task<IActionResult> ReserveRestriction(AuctionReserveRestrictionRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(request, ct));

    [HttpPost("auction-watchlist-bids")]
    public async Task<IActionResult> Watchlist(AuctionWatchlistBidRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(request, ct));

    [HttpPost("auction-report-resolved")]
    public async Task<IActionResult> Report(AuctionReportResolvedRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(request, ct));

}
