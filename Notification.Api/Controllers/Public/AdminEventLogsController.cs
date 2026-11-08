namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Mvc;
using Notification.Api.Filters;
using Notification.Application.Services.EventLogs;

[ApiController]
[Route("api/v1/admin/notifications/event-logs")]
[RequireRole("admin", "staff")]
public sealed class AdminEventLogsController(IEventLogService service) : ControllerBase
{
    /// <summary>Lists event log summaries with filters and pagination.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ListEventLogsRequest request, CancellationToken cancellationToken)
        => Ok(await service.ListAsync(request, cancellationToken));

    /// <summary>Retrieves the payload and correlation metadata of an event log.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await service.GetAsync(id, cancellationToken));
}
