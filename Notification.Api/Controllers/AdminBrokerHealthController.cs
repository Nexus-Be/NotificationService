
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Application.Services.Notifications;
using Notification.Domain.Enums;

namespace Notification.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/v1/admin/health/broker")]
public sealed class AdminBrokerHealthController : ControllerBase
{
    private readonly IBrokerHealthService _service;

    public AdminBrokerHealthController(
        IBrokerHealthService service)
    {
        _service = service;
    }

    // NOTI-16: Check RabbitMQ Broker Health
    [HttpGet]
    public async Task<IActionResult> GetBrokerHealth(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate limit
        if (limit < 1 || limit > 100)
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_LIMIT",
                detail: "Limit must be between 1 and 100.");
        }

        // 2. Check RabbitMQ and get history
        var result = await _service.GetAsync(
            limit,
            cancellationToken);

        // 3. Return response
        return Ok(new
        {
            success = true,
            statusCode = 200,
            data = new
            {
                currentStatus = ToApiStatus(
                    result.CurrentStatus),

                lastCheckedAt = result.LastCheckedAt,

                latestLatencyMs = result.LatestLatencyMs,

                latestQueueDepth = result.LatestQueueDepth,

                history = result.History.Select(h => new
                {
                    id = h.Id,
                    brokerName = h.BrokerName,
                    status = ToApiStatus(h.Status),
                    latencyMs = h.LatencyMs,
                    queueDepth = h.QueueDepth,
                    checkedAt = h.CheckedAt
                })
            },
            traceId = HttpContext.TraceIdentifier,
            timestamp = DateTimeOffset.UtcNow
        });
    }

    private static string ToApiStatus(
        BrokerHealthStatus status)
    {
        return status.ToString().ToUpperInvariant();
    }
}
