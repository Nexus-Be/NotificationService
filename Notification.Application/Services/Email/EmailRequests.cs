namespace Notification.Application.Services.Email;

using System.ComponentModel.DataAnnotations;

public sealed record SendOtpEmailRequest(
    [property: Required(ErrorMessage = "Email is required.")]
    [property: EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    string Email,
    [property: Required(ErrorMessage = "Full name is required.")]
    string FullName,
    [property: Required(ErrorMessage = "OTP code is required.")]
    string OtpCode,
    [property: Range(1, 60, ErrorMessage = "Expiry minutes must be between 1 and 60.")]
    int ExpiresInMinutes = 5
);

public sealed record SendGeneralEmailRequest(
    [property: Required(ErrorMessage = "Recipient email is required.")]
    [property: EmailAddress(ErrorMessage = "Recipient email must be a valid email address.")]
    string ToEmail,
    [property: Required(ErrorMessage = "Subject is required.")]
    string Subject,
    [property: Required(ErrorMessage = "HTML body is required.")]
    string HtmlBody
);
