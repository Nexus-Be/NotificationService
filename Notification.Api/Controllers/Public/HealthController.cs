namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Mvc;
using Notification.Api.Filters;
using Notification.Application.Services.Health;

[ApiController]
[Route("api/v1/admin/notifications/health-checks")]
[RequireRole("admin", "staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HealthController(IHealthService service) : ControllerBase
{
    /// <summary>Checks database readiness and the current process worker lifecycle.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(HealthDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthDetailsResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var response = await service.CheckAsync(cancellationToken);
        return StatusCode(response.IsReady ? 200 : 503,response);
    }
}
