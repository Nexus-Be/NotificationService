namespace Notification.Application.Services.ShipmentEvents;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateShipmentEventRequest
{
    [Required] public Guid? SourceEventId { get; init; }
    [Required] public Guid? ShipmentId { get; init; }
    [Required] public long? SourceVersion { get; init; }
    [Required] public Guid? OrderId { get; init; }
    [Required] public Guid? ShopId { get; init; }
    [Required] public Guid? BuyerUserId { get; init; }
    [Required] public Guid? SellerUserId { get; init; }
    public Guid? AuctionId { get; init; }
    [Required,RegularExpression("(?i)^(Regular|Auction)$")] public string OrderKind { get; init; } = string.Empty;
    [Required,StringLength(100)] public string OrderNo { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^GHN$")] public string Carrier { get; init; } = string.Empty;
    [Required,RegularExpression("^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$")] public string TrackingCode { get; init; } = string.Empty;
    [Required,RegularExpression("(?i)^(Created|PickedUp|InTransit|OutForDelivery|Delivered|DeliveryFailed|Returning|Returned|Cancelled)$")] public string Status { get; init; } = string.Empty;
    [StringLength(500)] public string? Reason { get; init; }
    [Required] public DateTimeOffset? PaidAt { get; init; }
    [Required] public DateTimeOffset? SellerAcceptedAt { get; init; }
    [Required] public DateTimeOffset? ShipmentCreatedAt { get; init; }
    [Required] public DateTimeOffset? OccurredAt { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
}
