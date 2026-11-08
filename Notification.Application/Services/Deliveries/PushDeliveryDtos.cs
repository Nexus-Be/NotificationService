namespace Notification.Application.Services.Deliveries;

public sealed record PushDeliveryMessage(Guid DeliveryId, Guid NotificationId, Guid UserId, string Token, string Title, string Body);

public sealed record PushDeliveryResult(bool Succeeded, bool IsTransient, string? ErrorCode, string? ErrorMessage,
    bool InvalidateToken = false, TimeSpan? RetryAfter = null, string? ProviderMessageId = null)
{
    public static PushDeliveryResult Sent(string messageId) => new(true, false, null, null, ProviderMessageId: messageId);
    public static PushDeliveryResult Failure(string code, string message, bool transient = false,
        bool invalidateToken = false, TimeSpan? retryAfter = null) => new(false, transient, code, message, invalidateToken, retryAfter);
}
