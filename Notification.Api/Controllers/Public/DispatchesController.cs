namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notification.Application.Services.Dispatches;

[ApiController]
[Route("api/v1/notifications/dispatches")]
public sealed class DispatchesController : ControllerBase
{
    private readonly IDispatchService _service;
    public DispatchesController(IDispatchService service) => _service = service;

    /// <summary>Creates a notification delivery from an active template.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(DispatchResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateDispatchRequest request, CancellationToken cancellationToken)
        => Ok(await _service.CreateAsync(request, cancellationToken));
}
