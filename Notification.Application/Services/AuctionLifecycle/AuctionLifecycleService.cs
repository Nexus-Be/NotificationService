namespace Notification.Application.Services.AuctionLifecycle;

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

public sealed class AuctionLifecycleService(INotificationDbContext db) : IAuctionLifecycleService
{
    public async Task<AuctionLifecycleResponse> CreateAsync(AuctionLifecycleRequest request, CancellationToken ct)
    {
        if (request.SourceEventId == Guid.Empty || request.AuctionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.AuctionTitle) || request.AuctionTitle.Length > 200 ||
            request.TraceId?.Length > 100)
            throw Invalid();

        var (userId, type, title, body, details) = Describe(request);
        if (userId == Guid.Empty) throw Invalid();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lockKey = $"auction-lifecycle:{request.SourceEventId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 8016))", ct);
        if (request is AuctionCancelledRequest)
        {
            var cancellationKey = $"auction-cancel-seller:{request.AuctionId:N}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({cancellationKey}, 8016))", ct);
        }

        var existing = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.SourceEventId == request.SourceEventId && n.TemplateId == null &&
                (n.NotificationType == NotificationType.AuctionFailed ||
                 n.NotificationType == NotificationType.AuctionCancelled ||
                 n.NotificationType == NotificationType.AuctionWinnerOffer))
            .OrderBy(n => n.CreatedAt).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            if (existing.UserId != userId || existing.NotificationType != type)
                throw new DomainException("SOURCE_EVENT_CONFLICT", "Source event is already associated with a different notification.", 409);
            return new(request.SourceEventId, existing.Id);
        }

        if (request is AuctionCancelledRequest)
        {
            var legacyNotification = await (
                from recipient in db.AuctionCancellationRecipients.IgnoreQueryFilters()
                join dispatch in db.AuctionCancellationDispatches.IgnoreQueryFilters()
                    on recipient.SourceEventId equals dispatch.Id
                where dispatch.AuctionId == request.AuctionId && recipient.UserId == userId
                select (Guid?)recipient.NotificationId).FirstOrDefaultAsync(ct);
            if (legacyNotification.HasValue)
                return new(request.SourceEventId, legacyNotification.Value);
        }

        var now = DateTimeOffset.UtcNow;
        var record = new NotificationRecord
        {
            Id = Guid.NewGuid(), UserId = userId, SourceEventId = request.SourceEventId,
            NotificationType = type, ChannelScope = ChannelScope.Specific,
            Title = title, Body = body,
            Payload = JsonSerializer.Serialize(details), Status = NotificationStatus.Completed,
            CreatedAt = now, UpdatedAt = now
        };
        db.Notifications.Add(record);
        db.NotificationDeliveries.Add(new NotificationDelivery
        {
            Id = Guid.NewGuid(), NotificationId = record.Id, ChannelCode = ChannelType.InApp,
            Destination = userId.ToString(), Status = DeliveryStatus.Sent, AttemptCount = 1,
            LastAttemptAt = now, RowVersion = 1, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(request.SourceEventId, record.Id);
    }

    private static (Guid, NotificationType, string, string, object) Describe(AuctionLifecycleRequest request) =>
        request switch
        {
            AuctionFailedRequest r when r.FailedAt != default && ValidReason(r.Reason) =>
                (r.SellerUserId, NotificationType.AuctionFailed, "Phiên đấu giá thất bại",
                 $"{r.AuctionTitle}: {r.Reason}",
                 new { auctionId = r.AuctionId, failedAt = r.FailedAt, traceId = r.TraceId }),
            AuctionCancelledRequest r when r.CancelledAt != default && r.Forced.HasValue && ValidReason(r.Reason) =>
                (r.SellerUserId, NotificationType.AuctionCancelled, "Phiên đấu giá đã hủy",
                 $"{r.AuctionTitle} được hủy bởi {(r.Forced.Value ? "quản trị viên" : "người bán")}. Lý do: {r.Reason}",
                 new { auctionId = r.AuctionId, cancelledAt = r.CancelledAt, forced = r.Forced.Value, traceId = r.TraceId }),
            AuctionWinnerOfferRequest r when r.OfferId != Guid.Empty && r.BidId != Guid.Empty &&
                r.SellerUserId != Guid.Empty && r.PreviousWinnerUserId != Guid.Empty &&
                r.BidderUserId != r.PreviousWinnerUserId && r.BidderUserId != r.SellerUserId &&
                r.Amount > 0 && r.Currency == "VND" && r.OfferedAt != default && r.ExpiresAt > r.OfferedAt =>
                (r.BidderUserId, NotificationType.AuctionWinnerOffer, "Lời mời thắng đấu giá thay thế",
                 $"{r.AuctionTitle}. Giá {r.Amount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))} VND. " +
                 $"Phản hồi trước {r.ExpiresAt.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm} (giờ Việt Nam).",
                 new { auctionId = r.AuctionId, offerId = r.OfferId, amount = r.Amount, currency = r.Currency,
                       expiresAt = r.ExpiresAt, traceId = r.TraceId }),
            _ => throw Invalid()
        };

    private static bool ValidReason(string reason) => !string.IsNullOrWhiteSpace(reason) && reason.Length <= 1000;
    private static DomainException Invalid() => new("VAL_INVALID_INPUT", "Auction notification data is invalid.", 400);
}
