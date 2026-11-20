namespace Notification.Application.Services.AuctionActivity;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

public sealed class AuctionActivityService(INotificationDbContext db, TimeProvider time) : IAuctionActivityService
{
    public async Task<AuctionActivityResponse> CreateAsync(IAuctionNotificationRequest request, CancellationToken ct)
    {
        if (request.SourceEventId == Guid.Empty || request.TraceId?.Length > 100)
            throw Invalid();
        if (request is AuctionActivityRequest activity &&
            (activity.AuctionId == Guid.Empty || !ValidText(activity.AuctionTitle, 200)))
            throw Invalid();

        request = Normalize(request);
        var (recipients, type, title, body, details) = Describe(request);
        if (recipients.Length is < 1 or > 1000 || recipients.Contains(Guid.Empty))
            throw Invalid();

        // Exclude trace context and normalize time zones and recipient order for replay.
        var snapshot = request switch
        {
            AuctionActivityRequest payloadActivity => JsonSerializer.Serialize(payloadActivity with { TraceId = null }, payloadActivity.GetType()),
            AuctionReserveRestrictionRequest restriction => JsonSerializer.Serialize(restriction with { TraceId = null }),
            _ => throw Invalid()
        };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var key = $"auction-activity:{request.SourceEventId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8030))", ct);
        var existing = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.SourceEventId == request.SourceEventId).OrderBy(n => n.UserId).ToListAsync(ct);
        if (existing.Count > 0)
        {
            if (existing.Count != recipients.Length ||
                !existing.Select(n => n.UserId).OrderBy(id => id).SequenceEqual(recipients.OrderBy(id => id)) ||
                existing.Any(n => n.NotificationType != type || !Matches(n.Payload, hash)))
                throw new DomainException("AUCTION_ACTIVITY_CONFLICT", "Source event has different notification data.", 409);
            return new(request.SourceEventId, true, existing.Select(n => n.Id).ToArray());
        }

        var channel = await db.NotificationChannels.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == ChannelType.InApp && !c.IsDeleted, ct)
            ?? throw new DomainException("CHANNEL_NOT_FOUND", "In-app channel was not found.", 409);
        var disabledUsers = await db.NotificationPreferences.AsNoTracking()
            .Where(p => recipients.Contains(p.UserId) && p.NotificationType == type &&
                p.ChannelCode == ChannelType.InApp && p.IsDisabled && !p.IsDeleted)
            .Select(p => p.UserId).ToListAsync(ct);
        var now = time.GetUtcNow();
        var ids = new List<Guid>();
        foreach (var user in recipients)
        {
            var visible = !channel.IsDisabled && !disabledUsers.Contains(user);
            var row = new NotificationRecord
            {
                Id = Guid.NewGuid(), UserId = user, SourceEventId = request.SourceEventId,
                NotificationType = type, ChannelScope = ChannelScope.Specific,
                Title = title, Body = body, Status = NotificationStatus.Completed,
                IsInboxVisible = visible, IsRead = !visible,
                Payload = JsonSerializer.Serialize(new
                {
                    type = type.ToString(), auctionId = (request as AuctionActivityRequest)?.AuctionId,
                    requestHash = hash, details, traceId = request.TraceId
                }),
                CreatedAt = now, UpdatedAt = now
            };
            db.Notifications.Add(row);
            db.NotificationDeliveries.Add(new NotificationDelivery
            {
                Id = Guid.NewGuid(), NotificationId = row.Id, ChannelCode = ChannelType.InApp,
                Destination = user.ToString(), Status = visible ? DeliveryStatus.Sent : DeliveryStatus.Skipped,
                AttemptCount = 1, LastAttemptAt = now, RowVersion = 1,
                CreatedAt = now, UpdatedAt = now
            });
            ids.Add(row.Id);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(request.SourceEventId, false, ids);
    }

    private static IAuctionNotificationRequest Normalize(IAuctionNotificationRequest request) => request switch
    {
        AuctionWatchlistBidRequest r => r with
        {
            WatcherUserIds = r.WatcherUserIds?.Distinct().OrderBy(id => id).ToArray() ?? [],
            OccurredAt = Utc(r.OccurredAt), AuctionEndsAt = Utc(r.AuctionEndsAt)
        },
        AuctionReportResolvedRequest r => r with
        {
            ReporterUserIds = r.ReporterUserIds?.Distinct().OrderBy(id => id).ToArray() ?? [],
            ResolvedAt = Utc(r.ResolvedAt)
        },
        AuctionDepositRequest r => r with { OccurredAt = Utc(r.OccurredAt) },
        AuctionReserveApprovalNeededRequest r => r with { ApprovalDeadlineAt = Utc(r.ApprovalDeadlineAt) },
        AuctionReserveRestrictionRequest r => r with
        {
            OccurredAt = Utc(r.OccurredAt),
            RestrictedUntil = r.RestrictedUntil.HasValue ? Utc(r.RestrictedUntil.Value) : null
        },
        _ => throw Invalid()
    };

    private static (Guid[], NotificationType, string, string, object) Describe(IAuctionNotificationRequest request) =>
        request switch
        {
            AuctionWatchlistBidRequest r when r.TriggerBidId != Guid.Empty && ValidAmount(r.NewAmount) &&
                r.Currency == "VND" && r.OccurredAt != default && r.AuctionEndsAt > r.OccurredAt =>
                (r.WatcherUserIds, NotificationType.AuctionWatchlistBid, "Phiên đang theo dõi có giá mới",
                    $"{r.AuctionTitle}: giá mới {Money(r.NewAmount)} VND.",
                    new { r.TriggerBidId, r.NewAmount, r.Currency, r.OccurredAt, r.AuctionEndsAt }),
            AuctionReportResolvedRequest r when r.Outcome is "ActionTaken" or "Dismissed" &&
                ValidText(r.Message, 1000) && (r.AdminReason is null || r.AdminReason.Length <= 1000) && r.ResolvedAt != default =>
                (r.ReporterUserIds, NotificationType.AuctionReportResolved, "Kết quả báo cáo phiên đấu giá",
                    r.Message, new { r.Outcome, r.ResolvedAt }),
            AuctionDepositRequest r when r.DepositId != Guid.Empty && r.BidderUserId != Guid.Empty &&
                ValidAmount(r.Amount) && r.Currency == "VND" && r.OccurredAt != default &&
                r.DepositStatus is "Authorized" or "Released" or "Captured" &&
                (r.Reason is null || ValidText(r.Reason, 200)) =>
                ([r.BidderUserId], NotificationType.AuctionDeposit, "Cập nhật tiền cọc đấu giá",
                    $"{r.AuctionTitle}: {DepositLabel(r.DepositStatus)} {Money(r.Amount)} VND.",
                    new { r.DepositId, r.Amount, r.Currency, r.DepositStatus, r.Reason, r.OccurredAt }),

            AuctionReserveApprovalNeededRequest r when r.SellerUserId != Guid.Empty &&
                ValidAmount(r.HighestBidAmount) && ValidAmount(r.ReservePriceAmount) &&
                r.HighestBidAmount < r.ReservePriceAmount && r.Currency == "VND" && r.ApprovalDeadlineAt != default =>
                ([r.SellerUserId], NotificationType.AuctionReserveApprovalNeeded, "Duyệt bán dưới giá sàn",
                    $"{r.AuctionTitle}: giá cao nhất {Money(r.HighestBidAmount)} VND, giá sàn {Money(r.ReservePriceAmount)} VND. " +
                    $"Hạn phản hồi: {r.ApprovalDeadlineAt.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm} (giờ Việt Nam). Kiểm tra trạng thái phiên trước khi phản hồi.",
                    new { highestBidAmount = r.HighestBidAmount, reservePriceAmount = r.ReservePriceAmount,
                        currency = r.Currency, approvalDeadlineAt = r.ApprovalDeadlineAt }),
            AuctionReserveRestrictionRequest r when r.SellerUserId != Guid.Empty &&
                r.ConsecutiveDeclineCount == 2 && r.IsRestricted == false &&
                r.RestrictedUntil is null && r.OccurredAt != default =>
                ([r.SellerUserId], NotificationType.AuctionReserveRestriction, "Cảnh báo quyền đặt giá sàn",
                    "Bạn đã từ chối bán dưới giá sàn hai lần liên tiếp. Nếu từ chối thêm một lần, quyền đặt giá sàn sẽ bị khóa 14 ngày.",
                    new { consecutiveDeclineCount = r.ConsecutiveDeclineCount, isRestricted = false,
                        restrictedUntil = r.RestrictedUntil, occurredAt = r.OccurredAt }),
            AuctionReserveRestrictionRequest r when r.SellerUserId != Guid.Empty &&
                r.ConsecutiveDeclineCount >= 3 && r.IsRestricted == true &&
                r.OccurredAt != default && r.RestrictedUntil > r.OccurredAt =>
                ([r.SellerUserId], NotificationType.AuctionReserveRestriction, "Tạm khóa quyền đặt giá sàn",
                    $"Quyền đặt giá sàn bị tạm khóa đến {r.RestrictedUntil.Value.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm} (giờ Việt Nam).",
                    new { consecutiveDeclineCount = r.ConsecutiveDeclineCount, isRestricted = true,
                        restrictedUntil = r.RestrictedUntil, occurredAt = r.OccurredAt }),
            _ => throw Invalid()
        };

    private static bool Matches(string? payload, string hash)
    {
        if (payload is null) return false;
        using var json = JsonDocument.Parse(payload);
        return json.RootElement.TryGetProperty("requestHash", out var stored) && stored.GetString() == hash;
    }

    private static string DepositLabel(string status) => status switch
    {
        "Authorized" => "đã giữ cọc",
        "Released" => "đã giải phóng cọc",
        "Captured" => "đã thu cọc",
        _ => throw Invalid()
    };

    private static string Money(decimal amount) => amount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
    private static bool ValidAmount(decimal value) =>
        value > 0 && value <= 9999999999999999.99m && decimal.Round(value, 2) == value;
    private static bool ValidText(string? value, int length) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= length && !value.Any(char.IsControl);
    private static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();
    private static DomainException Invalid() => new("VAL_INVALID_INPUT", "Auction activity fields are invalid.", 400);
}
