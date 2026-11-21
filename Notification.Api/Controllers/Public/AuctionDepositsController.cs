namespace Notification.Api.Controllers.Public;

using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Extensions;
using Notification.Application.Services.AuctionActivity;
using Notification.Application.Services.WalletEvents;
using Notification.Domain.Exceptions;

[ApiController]
[Route("api/v1/notifications/auction-deposits")]
[Authorize(Policy = WalletNotificationAuth.DepositPolicy)]
[RequestSizeLimit(16384)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuctionDepositsController(IAuctionActivityService auctions, IWalletEventService wallet) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Accepts the strict deposit contract selected by verified Auction or Wallet identity.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JsonElement request, CancellationToken ct)
    {
        try
        {
            if (User.HasClaim(WalletNotificationAuth.CallerClaim, "wallet"))
                return Ok(await wallet.CreateDepositAsync(request.Deserialize<WalletDepositEventRequest>(Json) ?? throw Invalid(), ct));
            return Ok(await auctions.CreateAsync(request.Deserialize<AuctionDepositRequest>(Json) ?? throw Invalid(), ct));
        }
        catch (JsonException) { throw Invalid(); }
    }

    private static DomainException Invalid() => new("VAL_INVALID_INPUT", "Deposit notification fields are invalid.", 400);
}
