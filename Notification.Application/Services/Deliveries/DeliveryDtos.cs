namespace Notification.Application.Services.Deliveries;

public sealed record EmailDeliveryMessage(Guid DeliveryId, string Recipient, string Subject, string HtmlBody)
{
    public string MessageId => $"delivery-{DeliveryId:N}@notifications.nexus";
}

public sealed record EmailDeliveryResult(bool Succeeded, bool IsTransient, string? ErrorCode, string? ErrorMessage,
    string? ProviderMessageId = null)
{
    public static EmailDeliveryResult Sent(string? providerMessageId = null) => new(true, false, null, null, providerMessageId);
    public static EmailDeliveryResult Failure(string code, string message, bool transient = false)
        => new(false, transient, code, message);
}
