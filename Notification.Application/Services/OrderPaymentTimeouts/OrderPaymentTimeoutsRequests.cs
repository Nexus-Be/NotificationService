namespace Notification.Application.Services.OrderPaymentTimeouts;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOrderPaymentTimeoutRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? OrderId { get; init; }
    [Required] public Guid? BuyerUserId { get; init; }
    [Required, RegularExpression("(?i)^Regular$")] public string OrderKind { get; init; } = string.Empty;
    [Required, StringLength(100)] public string OrderNo { get; init; } = string.Empty;
    [Required, RegularExpression("(?i)^Cancelled$")] public string OrderStatus { get; init; } = string.Empty;
    [Required, RegularExpression("(?i)^Expired$")] public string PaymentStatus { get; init; } = string.Empty;
    [Required] public DateTimeOffset? OrderCreatedAt { get; init; }
    [Required] public DateTimeOffset? PaymentDeadline { get; init; }
    [Required] public DateTimeOffset? CancelledAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
