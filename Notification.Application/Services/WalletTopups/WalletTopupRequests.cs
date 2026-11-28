namespace Notification.Application.Services.WalletTopups;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WalletTopupRequest(
    [Required, StringLength(100)] string SourceEventId,
    Guid TopupId,
    Guid TransactionId,
    Guid RecipientUserId,
    decimal Amount,
    [Required] string Currency,
    DateTimeOffset ConfirmedAt,
    [StringLength(100)] string? TraceId);

public sealed record WalletTopupDeliveryResponse(Guid DeliveryId, string Channel, string Status);

public sealed record WalletTopupResponse(
    string SourceEventId,
    Guid NotificationId,
    bool IsReplay,
    IReadOnlyList<WalletTopupDeliveryResponse> Deliveries);
