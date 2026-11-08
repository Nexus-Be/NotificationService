namespace Notification.Api.Controllers.Public;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Filters;
using Notification.Application.Services.AccountStatusChanges;

[ApiController]
[Route("api/v1/notifications/account-status-changes")]
[RequireRole("service","admin")]
[ResponseCache(NoStore = true,Location = ResponseCacheLocation.None)]
public sealed class AccountStatusChangesController(IAccountStatusChangeService service) : ControllerBase
{
    /// <summary>Accepts a ban or unban email after the source account status commits.</summary>
    [HttpPost]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(AccountStatusChangeResponse),StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateAccountStatusChangeRequest request,CancellationToken cancellationToken)
        => Ok(await service.CreateAsync(request,cancellationToken));
}
