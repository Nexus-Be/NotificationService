namespace Notification.Api.Controllers.Internal;

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Notification.Application.Services.Email;

[ApiController]
[Route("api/v1/internal/notifications/email")]
public sealed class InternalEmailController : ControllerBase
{
    private readonly IEmailNotificationService _emailNotificationService;
    private readonly ILogger<InternalEmailController> _logger;

    public InternalEmailController(
        IEmailNotificationService emailNotificationService,
        ILogger<InternalEmailController> logger)
    {
        _emailNotificationService = emailNotificationService;
        _logger = logger;
    }

    /// <summary>Sends OTP verification email synchronously.</summary>
    [HttpPost("otp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendOtpEmail(
        [FromBody] SendOtpEmailApiRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        {
            return BadRequest(new
            {
                errorCode = "VAL_INVALID_EMAIL",
                message = "Recipient email is required and must be a valid email address."
            });
        }

        if (string.IsNullOrWhiteSpace(request.OtpCode))
        {
            return BadRequest(new
            {
                errorCode = "VAL_INVALID_OTP",
                message = "OTP code cannot be empty."
            });
        }

        var expiresInMinutes = request.ExpiresInMinutes > 0 ? request.ExpiresInMinutes : 5;
        var fullName = string.IsNullOrWhiteSpace(request.FullName) ? "Customer" : request.FullName.Trim();

        _logger.LogInformation(
            "[REST Email] Dispatching OTP email to '{Email}' for user '{FullName}'.",
            request.Email,
            fullName);

        var dto = new SendOtpEmailDto(
            request.Email.Trim(),
            fullName,
            request.OtpCode.Trim(),
            expiresInMinutes);

        var sent = await _emailNotificationService.SendOtpEmailAsync(dto, cancellationToken);

        if (!sent)
        {
            _logger.LogWarning("[REST Email] Failed to send OTP email to '{Email}'.", request.Email);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                errorCode = "EMAIL_SEND_FAILED",
                message = "Failed to deliver OTP email via notification provider."
            });
        }

        return Ok(new
        {
            success = true,
            message = "OTP verification email dispatched successfully.",
            data = new
            {
                recipient = request.Email.Trim(),
                fullName,
                expiresInMinutes,
                dispatchedAt = DateTimeOffset.UtcNow
            }
        });
    }

    /// <summary>Sends a general HTML email synchronously.</summary>
    [HttpPost("send")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendGeneralEmail(
        [FromBody] SendGeneralEmailApiRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ToEmail) || !request.ToEmail.Contains('@'))
        {
            return BadRequest(new
            {
                errorCode = "VAL_INVALID_EMAIL",
                message = "Recipient email is required and must be a valid email address."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            return BadRequest(new
            {
                errorCode = "VAL_INVALID_INPUT",
                message = "Email subject is required."
            });
        }

        if (string.IsNullOrWhiteSpace(request.HtmlBody))
        {
            return BadRequest(new
            {
                errorCode = "VAL_INVALID_INPUT",
                message = "Email HTML content is required."
            });
        }

        _logger.LogInformation(
            "[REST Email] Dispatching general email to '{ToEmail}' with subject '{Subject}'.",
            request.ToEmail,
            request.Subject);

        var dto = new SendGeneralEmailDto(
            request.ToEmail.Trim(),
            request.Subject.Trim(),
            request.HtmlBody);

        var sent = await _emailNotificationService.SendEmailAsync(dto, cancellationToken);

        if (!sent)
        {
            _logger.LogWarning("[REST Email] Failed to send general email to '{ToEmail}'.", request.ToEmail);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                errorCode = "EMAIL_SEND_FAILED",
                message = "Failed to deliver email via notification provider."
            });
        }

        return Ok(new
        {
            success = true,
            message = "Email dispatched successfully.",
            data = new
            {
                recipient = request.ToEmail.Trim(),
                subject = request.Subject.Trim(),
                dispatchedAt = DateTimeOffset.UtcNow
            }
        });
    }
}

public sealed record SendOtpEmailApiRequest
{
    [Required]
    public string Email { get; init; } = string.Empty;

    public string? FullName { get; init; }

    [Required]
    public string OtpCode { get; init; } = string.Empty;

    public int ExpiresInMinutes { get; init; } = 5;
}

public sealed record SendGeneralEmailApiRequest
{
    [Required]
    public string ToEmail { get; init; } = string.Empty;

    [Required]
    public string Subject { get; init; } = string.Empty;

    [Required]
    public string HtmlBody { get; init; } = string.Empty;
}
