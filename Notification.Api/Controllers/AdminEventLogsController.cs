
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Application.Services.Notifications;

namespace Notification.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/v1/admin/event-logs")]
public sealed class AdminEventLogsController : ControllerBase
{
    private readonly IEventLogService _service;

    public AdminEventLogsController(IEventLogService service)
    {
        _service = service;
    }

    // NOTI-13: Get event logs
    [HttpGet]
    public async Task<IActionResult> GetEventLogs(
        [FromQuery] string? sourceService,
        [FromQuery] string? eventType,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate pagination
        if (pageIndex < 1 ||
            pageSize < 1 ||
            pageSize > 100 ||
            ((long)pageIndex - 1) * pageSize > int.MaxValue)
        {
            return InvalidFilter();
        }

        // 2. Validate filters
        if ((sourceService is not null &&
             (string.IsNullOrWhiteSpace(sourceService) ||
              sourceService.Length > 100)) ||
            (eventType is not null &&
             (string.IsNullOrWhiteSpace(eventType) ||
              eventType.Length > 150)))
        {
            return InvalidFilter();
        }

        // 3. Query Application Service
        var result = await _service.GetAsync(
            sourceService,
            eventType,
            pageIndex,
            pageSize,
            cancellationToken);

        // 4. Calculate pagination
        var totalPages = result.TotalCount == 0
            ? 0
            : (result.TotalCount - 1) / pageSize + 1;

        // 5. Return response
        return Ok(new
        {
            success = true,
            statusCode = 200,
            data = new
            {
                items = result.Items.Select(e => new
                {
                    id = e.Id,
                    eventId = e.EventId,
                    eventType = e.EventType,
                    sourceService = e.SourceService,
                    actorId = e.ActorId,
                    occurredAt = e.OccurredAt
                }),
                pagination = new
                {
                    pageIndex,
                    pageSize,
                    totalCount = result.TotalCount,
                    totalPages,
                    hasPreviousPage = pageIndex > 1,
                    hasNextPage = pageIndex < totalPages
                }
            },
            traceId = HttpContext.TraceIdentifier,
            timestamp = DateTimeOffset.UtcNow
        });
    }


    // NOTI-14: Get event log details
    [HttpGet("{id}")]
    public async Task<IActionResult> GetEventLogById(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        // 1. Validate Event Log ID
        if (!Guid.TryParse(id, out var eventLogId)
            || eventLogId == Guid.Empty)
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_EVENT_LOG_ID",
                detail: "Event log ID is invalid.");
        }

        // 2. Get event log from database
        var result = await _service.GetByIdAsync(
            eventLogId,
            cancellationToken);

        // 3. Handle not found
        if (result is null)
        {
            return Problem(
                statusCode: 404,
                title: "EVENT_LOG_NOT_FOUND",
                detail: "Event log was not found.");
        }

        // 4. Return response
        return Ok(new
        {
            success = true,
            statusCode = 200,
            message = "Event log retrieved successfully.",
            data = new
            {
                id = result.Id,
                eventId = result.EventId,
                eventType = result.EventType,
                sourceService = result.SourceService,
                actorId = result.ActorId,

                // Return only approved payload fields
                payload = EventLogPayloadSanitizer.Sanitize(
                    result.EventType,
                    result.PayloadJson),

                // Original payload has been filtered
                payloadRedacted =
                    !string.IsNullOrWhiteSpace(
                        result.PayloadJson),

                occurredAt = result.OccurredAt,
                createdAt = result.CreatedAt
            },
            traceId = HttpContext.TraceIdentifier,
            timestamp = DateTimeOffset.UtcNow
        });
    }

    private IActionResult InvalidFilter()
    {
        return Problem(
            statusCode: 400,
            title: "VAL_INVALID_FILTER",
            detail: "Query date range or filter is invalid.");
    }
}
