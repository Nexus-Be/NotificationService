namespace Notification.Api.Controllers.Internal;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Notification.Application.Services.Health;

/// <summary>Detailed readiness (database and every background worker) for the central health view. The internal service key is required by the pipeline.</summary>
[ApiController]
[Route("api/v1/internal/health")]
public sealed class InternalHealthController : ControllerBase
{
    private readonly IHealthService _health;

    public InternalHealthController(IHealthService health)
    {
        _health = health;
    }

    /// <summary>Returns the database state and one entry per worker.</summary>
    [HttpGet("workers")]
    public async Task<IActionResult> GetWorkers(CancellationToken cancellationToken)
    {
        var details = await _health.CheckAsync(cancellationToken);
        return Ok(details);
    }
}
