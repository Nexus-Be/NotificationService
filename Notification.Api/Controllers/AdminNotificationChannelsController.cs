
using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Application.Services.Notifications;
using Notification.Domain.Enums;

namespace Notification.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/v1/admin/notifications/channels")]
public sealed class AdminNotificationChannelsController
    : ControllerBase
{
    private readonly INotificationChannelService _service;

    public AdminNotificationChannelsController(
        INotificationChannelService service)
    {
        _service = service;
    }

    // NOTI-07: Get notification channel
    [HttpGet("{code}")]
    public async Task<IActionResult> GetChannel(
        [FromRoute] string code,
        CancellationToken cancellationToken)
    {
        // 1. Validate channel code
        ChannelType? channelType =
            code.Trim().ToUpperInvariant() switch
            {
                "EMAIL" => ChannelType.Email,
                "PUSH" => ChannelType.Push,
                "IN_APP" => ChannelType.InApp,
                "SMS" => ChannelType.Sms,
                _ => null
            };

        if (channelType is null)
        {
            return Problem(
                statusCode: 404,
                title: "CHANNEL_NOT_FOUND",
                detail: "Notification channel code was not found.");
        }

        // 2. Get channel from database
        var channel = await _service.GetAsync(
            channelType.Value,
            cancellationToken);

        if (channel is null)
        {
            return Problem(
                statusCode: 404,
                title: "CHANNEL_NOT_FOUND",
                detail: "Notification channel code was not found.");
        }

        // 3. Return response
        return Ok(new
        {
            success = true,
            statusCode = 200,
            data = new
            {
                id = channel.Id,
                code = ToApiCode(channel.Code),
                name = channel.Name,
                provider = channel.Provider?.ToString()
                    .ToUpperInvariant(),
                config = channel.SafeConfigJson is null
                    ? null
                    : JsonNode.Parse(channel.SafeConfigJson),
                isEnabled = channel.IsEnabled,
                updatedAt = channel.UpdatedAt
            },
            traceId = HttpContext.TraceIdentifier,
            timestamp = DateTimeOffset.UtcNow
        });
    }


    // NOTI-07: Update notification channel
    [HttpPut("{code}")]
    public async Task<IActionResult> UpdateChannel(
        [FromRoute] string code,
        [FromBody] UpdateAdminNotificationChannelRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Validate channel code
        ChannelType? channelType =
            (code ?? "").Trim().ToUpperInvariant() switch
            {
                "EMAIL" => ChannelType.Email,
                "PUSH" => ChannelType.Push,
                "IN_APP" => ChannelType.InApp,
                "SMS" => ChannelType.Sms,
                _ => null
            };

        if (channelType is null)
        {
            return Problem(
                statusCode: 404,
                title: "CHANNEL_NOT_FOUND",
                detail: "Notification channel code was not found.");
        }

        // 2. Validate request
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Length > 150 ||
            request.IsEnabled is null ||
            (request.Config.HasValue &&
             request.Config.Value.ValueKind != JsonValueKind.Object))
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_INPUT",
                detail: "Channel configuration is invalid.");
        }

        // 3. Parse provider
        ChannelProvider? provider = null;

        if (!string.IsNullOrWhiteSpace(request.Provider))
        {
            if (!Enum.TryParse<ChannelProvider>(
                    request.Provider.Trim(),
                    true,
                    out var parsed) ||
                !Enum.IsDefined(parsed) ||
                !string.Equals(
                    parsed.ToString(),
                    request.Provider.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Problem(
                    statusCode: 400,
                    title: "VAL_INVALID_PROVIDER",
                    detail: "Channel provider is invalid.");
            }

            provider = parsed;
        }

        // 4. Update database
        NotificationChannelDto? result;

        try
        {
            result = await _service.UpdateAsync(
                channelType.Value,
                new UpdateNotificationChannelDto(
                    request.Name.Trim(),
                    provider,
                    request.Config?.GetRawText(),
                    request.IsEnabled.Value),
                cancellationToken);
        }
        catch (ArgumentException)
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_INPUT",
                detail: "Channel configuration is invalid.");
        }
        catch (InvalidOperationException ex)
            when (ex.Message == "SECRET_IN_CONFIG_FORBIDDEN")
        {
            return Problem(
                statusCode: 400,
                title: "SECRET_IN_CONFIG_FORBIDDEN",
                detail: "Secrets are not allowed in config body.");
        }

        // 5. Handle not found
        if (result is null)
        {
            return Problem(
                statusCode: 404,
                title: "CHANNEL_NOT_FOUND",
                detail: "Notification channel was not found.");
        }

        // 6. Return response
        return Ok(new
        {
            success = true,
            statusCode = 200,
            data = new
            {
                id = result.Id,
                code = ToApiCode(result.Code),
                name = result.Name,
                provider = result.Provider?.ToString()
                    .ToUpperInvariant(),
                config = result.SafeConfigJson is null
                    ? null
                    : JsonNode.Parse(result.SafeConfigJson),
                isEnabled = result.IsEnabled,
                updatedAt = result.UpdatedAt
            },
            traceId = HttpContext.TraceIdentifier,
            timestamp = DateTimeOffset.UtcNow
        });
    }

    private static string ToApiCode(ChannelType code)
    {
        return code switch
        {
            ChannelType.Email => "EMAIL",
            ChannelType.Push => "PUSH",
            ChannelType.InApp => "IN_APP",
            ChannelType.Sms => "SMS",
            _ => throw new ArgumentOutOfRangeException(
                nameof(code))
        };
    }

    public sealed class UpdateAdminNotificationChannelRequest
    {
        public string? Name { get; init; }

        public string? Provider { get; init; }

        public JsonElement? Config { get; init; }

        public bool? IsEnabled { get; init; }
    }
}
