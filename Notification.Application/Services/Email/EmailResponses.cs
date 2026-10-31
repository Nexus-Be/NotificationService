namespace Notification.Application.Services.Email;

public sealed record EmailSendResponse(
    bool Success,
    string Message
);
