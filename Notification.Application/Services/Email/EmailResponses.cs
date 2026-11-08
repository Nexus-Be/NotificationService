namespace Notification.Application.Services.Email;

public sealed record OtpEmailDispatchResponse(
    string Recipient, string FullName, int ExpiresInMinutes, string Purpose,
    string Status, DateTimeOffset AcceptedAt, Guid? RequestId = null, DateTimeOffset? ExpiresAt = null, bool IsReplay = false);

public sealed record EmailDispatchResponse(string Recipient, string Status, DateTimeOffset DispatchedAt);

public sealed record EmailSendResponse(bool Success, string Message);
