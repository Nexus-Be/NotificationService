namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Mvc;
using Notification.Api.Filters;
using Notification.Application.Services.EventLogs;

[ApiController]
[Route("api/v1/notifications/event-logs")]
[RequireRole("service", "admin")]
public sealed class EventLogsController(IEventLogService service) : ControllerBase
{
    /// <summary>Records an idempotent event envelope from a trusted producer.</summary>
    [HttpPost]
    [RequestSizeLimit(131072)]
    public async Task<IActionResult> Create([FromBody] CreateEventLogRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return StatusCode(result.IsReplay ? 200 : 201, result);
    }
}
