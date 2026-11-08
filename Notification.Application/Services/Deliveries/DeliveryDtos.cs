namespace Notification.Application.Services.Deliveries;

public sealed record EmailDeliveryMessage(Guid DeliveryId, string Recipient, string Subject, string HtmlBody)
{
    public string MessageId => $"delivery-{DeliveryId:N}@notifications.nexus";
}

public sealed record EmailDeliveryResult(bool Succeeded, bool IsTransient, string? ErrorCode, string? ErrorMessage)
{
    public static EmailDeliveryResult Sent() => new(true, false, null, null);
    public static EmailDeliveryResult Failure(string code, string message, bool transient = false)
        => new(false, transient, code, message);
}
