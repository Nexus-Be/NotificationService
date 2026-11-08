namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Mvc;
using Notification.Api.Extensions;
using Notification.Api.Filters;
using Notification.Application.Services.Management;

[ApiController]
[Route("api/v1/admin/notifications")]
[RequireRole("admin", "staff")]
public sealed class NotificationManagementController : ControllerBase
{
    private readonly INotificationManagementService _service;
    public NotificationManagementController(INotificationManagementService service) => _service = service;

    /// <summary>Lists notification templates with filters and pagination.</summary>
    [HttpGet("templates")]
    public async Task<IActionResult> Templates([FromQuery] TemplateListRequest request, CancellationToken cancellationToken)
        => Ok(await _service.ListTemplatesAsync(request, cancellationToken));

    /// <summary>Retrieves a notification template.</summary>
    [HttpGet("templates/{id:guid}")]
    public async Task<IActionResult> Template(Guid id, CancellationToken cancellationToken)
        => Ok(await _service.GetTemplateAsync(id, cancellationToken));

    /// <summary>Creates an audited notification template.</summary>
    [HttpPost("templates")]
    [RequireRole("admin")]
    public async Task<IActionResult> CreateTemplate([FromBody] CreateTemplateRequest request, CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId, out var error)) return error;
        return StatusCode(201, await _service.CreateTemplateAsync(request, Actor(userId), cancellationToken));
    }

    /// <summary>Updates mutable template settings with a version precondition.</summary>
    [HttpPut("templates/{id:guid}")]
    [RequireRole("admin")]
    public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] UpdateTemplateRequest request, CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId, out var error)) return error;
        return Ok(await _service.UpdateTemplateAsync(id, request, Actor(userId), cancellationToken));
    }

    /// <summary>Lists channels without provider credentials.</summary>
    [HttpGet("channels")]
    public async Task<IActionResult> Channels([FromQuery] ManagementPageRequest request, CancellationToken cancellationToken)
        => Ok(await _service.ListChannelsAsync(request, cancellationToken));

    /// <summary>Updates a channel name and enabled state.</summary>
    [HttpPut("channels/{code}")]
    [RequireRole("admin")]
    public async Task<IActionResult> UpdateChannel(string code, [FromBody] UpdateChannelRequest request, CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId, out var error)) return error;
        return Ok(await _service.UpdateChannelAsync(code, request, Actor(userId), cancellationToken));
    }

    /// <summary>Lists delivery outcomes without raw destinations.</summary>
    [HttpGet("deliveries")]
    public async Task<IActionResult> Deliveries([FromQuery] DeliveryListRequest request, CancellationToken cancellationToken)
        => Ok(await _service.ListDeliveriesAsync(request, cancellationToken));

    /// <summary>Retrieves a delivery and its aggregate notification state.</summary>
    [HttpGet("deliveries/{id:guid}")]
    public async Task<IActionResult> Delivery(Guid id, CancellationToken cancellationToken)
        => Ok(await _service.GetDeliveryAsync(id, cancellationToken));

    /// <summary>Lists sanitized delivery attempt logs.</summary>
    [HttpGet("deliveries/{id:guid}/logs")]
    public async Task<IActionResult> Logs(Guid id, [FromQuery] ManagementPageRequest request, CancellationToken cancellationToken)
        => Ok(await _service.ListLogsAsync(id, request, cancellationToken));

    /// <summary>Accepts an idempotent manual retry for a failed delivery.</summary>
    [HttpPost("deliveries/{id:guid}/retries")]
    [RequireRole("admin")]
    public async Task<IActionResult> Retry(Guid id, [FromBody] RetryDeliveryRequest request, CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId, out var error)) return error;
        return StatusCode(202, await _service.RetryAsync(id, request, Actor(userId), cancellationToken));
    }

    private ManagementActor Actor(Guid id) => new(id, this.GetTraceId(), this.GetClientIp(), Request.Headers.UserAgent.ToString());
}
