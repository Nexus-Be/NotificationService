namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notification.Api.Filters;
using Notification.Application.Services.PasswordChanges;

[ApiController]
[Route("api/v1/notifications/password-changes")]
[RequireRole("service","admin")]
[ResponseCache(NoStore = true,Location = ResponseCacheLocation.None)]
public sealed class PasswordChangesController(IPasswordChangeService service) : ControllerBase
{
    /// <summary>Accepts an email confirmation after a password change or reset has committed.</summary>
    [HttpPost]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(PasswordChangeResponse),StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreatePasswordChangeRequest request,CancellationToken cancellationToken)
        => Ok(await service.CreateAsync(request,cancellationToken));
}
