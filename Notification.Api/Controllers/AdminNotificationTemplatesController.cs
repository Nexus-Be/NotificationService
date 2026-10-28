
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Application.Services.Notifications;
using Notification.Domain.Enums;

namespace Notification.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/v1/admin/notifications/templates")]
public sealed class AdminNotificationTemplatesController
    : ControllerBase
{
    private readonly INotificationTemplateService _service;

    public AdminNotificationTemplatesController(
        INotificationTemplateService service)
    {
        _service = service;
    }

    // NOTI-08: Get notification templates
    [HttpGet]
    public async Task<IActionResult> GetTemplates(
        [FromQuery] string? channelCode,
        [FromQuery] string? notificationType,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate pagination
        if (pageIndex < 1 || pageSize < 1 || pageSize > 100)
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_PAGE",
                detail: "Invalid pagination parameters.");
        }

        // 2. Parse channel filter
        ChannelType? channel = null;

        if (channelCode is not null)
        {
            if (!TryParseApiCode(
                channelCode, out ChannelType parsed))
            {
                return Problem(
                    statusCode: 400,
                    title: "VAL_INVALID_CHANNEL",
                    detail: "Invalid channel code.");
            }

            channel = parsed;
        }

        // 3. Parse notification type filter
        NotificationType? type = null;

        if (notificationType is not null)
        {
            if (!TryParseApiCode(
                notificationType,
                out NotificationType parsed))
            {
                return Problem(
                    statusCode: 400,
                    title: "VAL_INVALID_NOTIFICATION_TYPE",
                    detail: "Invalid notification type.");
            }

            type = parsed;
        }

        // 4. Get templates from database
        var result = await _service.GetAsync(
            channel,
            type,
            pageIndex,
            pageSize,
            cancellationToken);

        // 5. Calculate pagination
        var totalPages = result.TotalCount == 0
            ? 0
            : (result.TotalCount - 1) / pageSize + 1;

        // 6. Return response
        return Ok(new
        {
            success = true,
            statusCode = 200,
            data = new
            {
                items = result.Items.Select(t => new
                {
                    id = t.Id,
                    code = t.Code,
                    notificationType = ToApiCode(
                        t.NotificationType),
                    channelCode = ToApiCode(t.ChannelCode),
                    subjectTemplate = t.SubjectTemplate,
                    bodyTemplate = t.BodyTemplate,
                    isMandatory = t.IsMandatory,
                    locale = t.Locale,
                    isActive = t.IsActive
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

    // Convert API string to enum safely
    private static bool TryParseApiCode<TEnum>(
        string code,
        out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;

        if (string.IsNullOrWhiteSpace(code))
            return false;

        var normalized = code.Trim().Replace("_", "");

        return Enum.TryParse(
                   normalized, true, out value)
               && Enum.IsDefined(value)
               && string.Equals(
                   ToApiCode(value),
                   code.Trim(),
                   StringComparison.OrdinalIgnoreCase);
    }


    // NOTI-08: Create notification template
    [HttpPost]
    public async Task<IActionResult> CreateTemplate(
        [FromBody] CreateAdminNotificationTemplateRequest? request,
        CancellationToken cancellationToken)
    {
        // 1. Validate request
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.NotificationType) ||
            string.IsNullOrWhiteSpace(request.ChannelCode) ||
            string.IsNullOrWhiteSpace(request.BodyTemplate) ||
            string.IsNullOrWhiteSpace(request.Locale) ||
            request.IsMandatory is null ||
            request.IsActive is null)
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_INPUT",
                detail: "Template request is invalid.");
        }

        // 2. Validate notification type
        if (!TryParseApiCode(
                request.NotificationType,
                out NotificationType notificationType))
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_NOTIFICATION_TYPE",
                detail: "Notification type is invalid.");
        }

        // 3. Validate channel code
        if (!TryParseApiCode(
                request.ChannelCode,
                out ChannelType channelCode))
        {
            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_CHANNEL",
                detail: "Channel code is invalid.");
        }

        // 4. Create template
        NotificationTemplateDto result;

        try
        {
            result = await _service.CreateAsync(
                new CreateNotificationTemplateDto(
                    request.Code,
                    notificationType,
                    channelCode,
                    request.SubjectTemplate,
                    request.BodyTemplate,
                    request.IsMandatory.Value,
                    request.Locale,
                    request.IsActive.Value),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            if (ex.Message.Contains(
                "INVALID_TEMPLATE_SYNTAX",
                StringComparison.Ordinal))
            {
                return Problem(
                    statusCode: 400,
                    title: "INVALID_TEMPLATE_SYNTAX",
                    detail: "Template syntax is invalid.");
            }

            return Problem(
                statusCode: 400,
                title: "VAL_INVALID_INPUT",
                detail: "Template data is invalid.");
        }
        catch (InvalidOperationException ex)
            when (ex.Message == "TEMPLATE_CODE_DUPLICATED")
        {
            return Problem(
                statusCode: 409,
                title: "TEMPLATE_CODE_DUPLICATED",
                detail: "Template code and locale already exist.");
        }

        // 5. Return HTTP 201 Created
        return StatusCode(201, new
        {
            success = true,
            statusCode = 201,
            data = new
            {
                id = result.Id,
                code = result.Code,
                notificationType = ToApiCode(
                    result.NotificationType),
                channelCode = ToApiCode(result.ChannelCode),
                subjectTemplate = result.SubjectTemplate,
                bodyTemplate = result.BodyTemplate,
                isMandatory = result.IsMandatory,
                locale = result.Locale,
                isActive = result.IsActive
            },
            traceId = HttpContext.TraceIdentifier,
            timestamp = DateTimeOffset.UtcNow
        });
    }

    // Convert enum to API format
    private static string ToApiCode<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return Regex.Replace(
            value.ToString(),
            "([a-z0-9])([A-Z])",
            "$1_$2").ToUpperInvariant();
    }

    public sealed class CreateAdminNotificationTemplateRequest
    {
        public string? Code { get; init; }

        public string? NotificationType { get; init; }

        public string? ChannelCode { get; init; }

        public string? SubjectTemplate { get; init; }

        public string? BodyTemplate { get; init; }

        public bool? IsMandatory { get; init; }

        public string? Locale { get; init; }

        public bool? IsActive { get; init; }
    }

}
