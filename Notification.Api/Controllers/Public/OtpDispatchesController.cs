namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Mvc;
using Notification.Api.Filters;
using Notification.Application.Services.Email;

[ApiController]
[Route("api/v1/notifications/otp-dispatches")]
[RequireRole("service","admin")]
[ResponseCache(NoStore = true,Location = ResponseCacheLocation.None)]
public sealed class OtpDispatchesController(IOtpEmailDispatchService service) : ControllerBase
{
    /// <summary>Retrieves safe OTP delivery metadata without the verification code.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id,CancellationToken cancellationToken) => Ok(await service.GetAsync(id,cancellationToken));
}
