namespace Notification.Application.Services.AuctionLifecycle;

using System.ComponentModel.DataAnnotations;

public abstract record AuctionLifecycleRequest(
    Guid SourceEventId, Guid AuctionId, [Required, StringLength(200)] string AuctionTitle,
    [StringLength(100)] string? TraceId);

public sealed record AuctionFailedRequest(Guid SourceEventId, Guid AuctionId, Guid SellerUserId,
    string AuctionTitle, [Required, StringLength(1000)] string Reason, DateTimeOffset FailedAt,
    string? TraceId = null) : AuctionLifecycleRequest(SourceEventId, AuctionId, AuctionTitle, TraceId);

public sealed record AuctionCancelledRequest(Guid SourceEventId, Guid AuctionId, Guid SellerUserId,
    string AuctionTitle, [Required, StringLength(1000)] string Reason, DateTimeOffset CancelledAt,
    [Required] bool? Forced, string? TraceId = null)
    : AuctionLifecycleRequest(SourceEventId, AuctionId, AuctionTitle, TraceId);

public sealed record AuctionWinnerOfferRequest(Guid SourceEventId, Guid AuctionId, Guid OfferId,
    Guid BidId, Guid BidderUserId, Guid PreviousWinnerUserId, Guid SellerUserId, string AuctionTitle,
    decimal Amount, [Required] string Currency, DateTimeOffset OfferedAt, DateTimeOffset ExpiresAt,
    string? TraceId = null) : AuctionLifecycleRequest(SourceEventId, AuctionId, AuctionTitle, TraceId);
