namespace Notification.Application.Services.Email;

public sealed record SendOtpEmailDto(
    string Email,
    string FullName,
    string OtpCode,
    int ExpiresInMinutes = 5,
    string Purpose = "registration");

public sealed record SendGeneralEmailDto(
    string ToEmail,
    string Subject,
    string HtmlBody);
