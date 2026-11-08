namespace Notification.Application.Services.Email;

using System.ComponentModel.DataAnnotations;

public sealed record SendOtpEmailRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    [StringLength(254)] public string Email { get; init; } = string.Empty;
    [StringLength(200)] public string? FullName { get; init; }
    [Required(ErrorMessage = "OTP code is required.")]
    [StringLength(32)] public string OtpCode { get; init; } = string.Empty;
    [Range(1, 60, ErrorMessage = "Expiry minutes must be between 1 and 60.")]
    public int ExpiresInMinutes { get; init; } = 5;
    [RegularExpression(@"(?i)^\s*(registration|password_reset|seller_registration)?\s*$", ErrorMessage = "OTP purpose must be registration, password_reset, or seller_registration.")]
    public string? Purpose { get; init; } = "registration";
    public Guid? RequestId { get; init; }
    public Guid? UserId { get; init; }
    public DateTimeOffset? IssuedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}

public sealed record SendGeneralEmailRequest
{
    [Required(ErrorMessage = "Recipient email is required.")]
    [EmailAddress(ErrorMessage = "Recipient email must be a valid email address.")]
    public string ToEmail { get; init; } = string.Empty;
    [Required(ErrorMessage = "Subject is required.")]
    public string Subject { get; init; } = string.Empty;
    [Required(ErrorMessage = "HTML body is required.")]
    public string HtmlBody { get; init; } = string.Empty;
}

public sealed record SendSellerRejectionEmailRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    public string Email { get; init; } = string.Empty;
    public string? FullName { get; init; }
    public string? ShopName { get; init; }
    [Required(ErrorMessage = "Rejection reason is required.")]
    public string RejectReason { get; init; } = string.Empty;
}
