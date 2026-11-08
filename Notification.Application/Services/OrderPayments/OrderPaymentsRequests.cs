namespace Notification.Application.Services.OrderPayments;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOrderPaymentRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? OrderId { get; init; }
    [Required] public Guid? ShopId { get; init; }
    [Required] public Guid? BuyerUserId { get; init; }
    [Required] public Guid? SellerUserId { get; init; }
    [Required,StringLength(100)] public string OrderNo { get; init; } = string.Empty;
    [Required] public decimal? TotalAmount { get; init; }
    [Required,RegularExpression("(?i)^VND$")] public string Currency { get; init; } = "VND";
    [Required] public DateTimeOffset? PaidAt { get; init; }
    [Required] public DateTimeOffset? SellerAcceptDeadline { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
