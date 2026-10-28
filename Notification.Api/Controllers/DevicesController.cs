
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Application.Services.Notifications;
using Notification.Domain.Enums;

namespace Notification.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/notifications/devices")]
public sealed class DevicesController : ControllerBase
{
    private readonly IDeviceTokenService _deviceTokenService;

    public DevicesController(IDeviceTokenService deviceTokenService)
    {
        _deviceTokenService = deviceTokenService;
    }

    // NOTI-01: Register or refresh device token
    [HttpPost]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] RegisterDeviceRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
            return Unauthorized();

        if (request is null ||
            string.IsNullOrWhiteSpace(request.Token) ||
            request.Token.Trim().Length > 1000 ||
            !Enum.TryParse<DevicePlatform>(
                request.Platform, true, out var platform) ||
            !Enum.IsDefined(platform))
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_DEVICE_TOKEN",
                detail: "Platform or token is invalid.");
        }

        DeviceTokenDto result;

        try
        {
            result = await _deviceTokenService.RegisterAsync(
                userId.Value,
                platform,
                request.Token,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
            when (ex.Message ==
                "Device token belongs to another active user.")
        {
            return Problem(
                statusCode: 409,
                title: "DEVICE_TOKEN_CONFLICT",
                detail: "Device token is already active for another user.");
        }

        return Ok(new
        {
            success = true,
            statusCode = 200,
            data = new
            {
                deviceId = result.DeviceId,
                userId = result.UserId,
                platform = result.Platform.ToString().ToUpperInvariant(),
                isActive = result.IsActive,
                lastSeenAt = result.LastSeenAt
            },
            traceId = HttpContext.TraceIdentifier,
            timestamp = DateTimeOffset.UtcNow
        });
    }

    // NOTI-02: Revoke device token
    [HttpDelete]
    public async Task<IActionResult> RevokeDevice(
        [FromBody] RevokeDeviceRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
            return Unauthorized();

        if (request is null ||
            string.IsNullOrWhiteSpace(request.Token) ||
            request.Token.Trim().Length > 1000)
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_TOKEN",
                detail: "Token is missing or invalid.");
        }

        var revoked = await _deviceTokenService.RevokeAsync(
            userId.Value,
            request.Token,
            cancellationToken);

        if (!revoked)
        {
            return Problem(
                statusCode: 404,
                title: "DEVICE_TOKEN_NOT_FOUND",
                detail: "Device token was not found for this user.");
        }

        return NoContent();
    }

    private Guid? GetCurrentUserId()
    {
        var userIdClaim =
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId)
            || userId == Guid.Empty)
        {
            return null;
        }

        return userId;
    }
}
