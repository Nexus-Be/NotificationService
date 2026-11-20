namespace Notification.Application.Services.AuctionActivity;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

public interface IAuctionNotificationRequest
{
    Guid SourceEventId { get; }
    string? TraceId { get; }
}

public abstract record AuctionActivityRequest(
    Guid SourceEventId, Guid AuctionId,
    [Required, StringLength(200)] string AuctionTitle,
    [StringLength(100)] string? TraceId) : IAuctionNotificationRequest;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuctionWatchlistBidRequest(
    Guid SourceEventId, Guid AuctionId, Guid TriggerBidId, string AuctionTitle,
    decimal NewAmount, string Currency, Guid[] WatcherUserIds,
    DateTimeOffset OccurredAt, DateTimeOffset AuctionEndsAt, string? TraceId = null)
    : AuctionActivityRequest(SourceEventId, AuctionId, AuctionTitle, TraceId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuctionReportResolvedRequest(
    Guid SourceEventId, Guid AuctionId, string AuctionTitle,
    string Outcome, string Message, string? AdminReason, Guid[] ReporterUserIds,
    DateTimeOffset ResolvedAt, string? TraceId = null)
    : AuctionActivityRequest(SourceEventId, AuctionId, AuctionTitle, TraceId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuctionDepositRequest(
    Guid SourceEventId, Guid AuctionId, Guid DepositId, Guid BidderUserId,
    string AuctionTitle, decimal Amount, string Currency, string DepositStatus,
    string? Reason, DateTimeOffset OccurredAt, string? TraceId = null)
    : AuctionActivityRequest(SourceEventId, AuctionId, AuctionTitle, TraceId);

public sealed record AuctionActivityResponse(
    Guid SourceEventId, bool IsReplay, IReadOnlyList<Guid> NotificationIds);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuctionReserveApprovalNeededRequest(
    Guid SourceEventId, Guid AuctionId, Guid SellerUserId, string AuctionTitle,
    decimal HighestBidAmount, decimal ReservePriceAmount, string Currency,
    DateTimeOffset ApprovalDeadlineAt, string? TraceId = null)
    : AuctionActivityRequest(SourceEventId, AuctionId, AuctionTitle, TraceId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuctionReserveRestrictionRequest(
    Guid SourceEventId, Guid SellerUserId, int ConsecutiveDeclineCount,
    [Required] bool? IsRestricted, DateTimeOffset? RestrictedUntil,
    DateTimeOffset OccurredAt, string? TraceId = null) : IAuctionNotificationRequest;
