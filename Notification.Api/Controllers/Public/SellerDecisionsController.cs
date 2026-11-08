namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Filters;
using Notification.Application.Services.SellerDecisions;

[ApiController]
[Route("api/v1/notifications/seller-decisions")]
[RequireRole("service","admin")]
[ResponseCache(NoStore = true,Location = ResponseCacheLocation.None)]
public sealed class SellerDecisionsController(ISellerDecisionService service) : ControllerBase
{
    /// <summary>Accepts a seller application decision email after the source decision commits.</summary>
    [HttpPost]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(SellerDecisionResponse),StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateSellerDecisionRequest request,CancellationToken cancellationToken)
        => Ok(await service.CreateAsync(request,cancellationToken));
}
