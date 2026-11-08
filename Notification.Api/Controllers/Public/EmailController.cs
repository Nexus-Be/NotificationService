namespace Notification.Api.Controllers.Public;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notification.Application.Services.Email;
using Notification.Api.Filters;

[ApiController]
[Route("api/v1/notifications/email")]
public sealed class EmailController : ControllerBase
{
    private readonly IEmailNotificationService _emailNotificationService;

    public EmailController(IEmailNotificationService emailNotificationService)
    {
        _emailNotificationService = emailNotificationService;
    }

    /// <summary>Accepts an OTP email request with its verification purpose.</summary>
    [HttpPost("otp")]
    [RequireRole("service", "admin")]
    [RequestSizeLimit(16384)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(OtpEmailDispatchResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SendOtpEmail([FromBody] SendOtpEmailRequest request, CancellationToken cancellationToken)
        => Ok(await _emailNotificationService.DispatchOtpAsync(request, cancellationToken));

    /// <summary>Sends an HTML email through the configured provider.</summary>
    [HttpPost("send")]
    [ProducesResponseType(typeof(EmailDispatchResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SendGeneralEmail([FromBody] SendGeneralEmailRequest request, CancellationToken cancellationToken)
        => Ok(await _emailNotificationService.DispatchEmailAsync(request, cancellationToken));

    /// <summary>Sends a seller registration rejection email.</summary>
    [HttpPost("seller-rejection")]
    [ProducesResponseType(typeof(EmailDispatchResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SendSellerRejectionEmail([FromBody] SendSellerRejectionEmailRequest request, CancellationToken cancellationToken)
        => Ok(await _emailNotificationService.DispatchSellerRejectionAsync(request, cancellationToken));
}
