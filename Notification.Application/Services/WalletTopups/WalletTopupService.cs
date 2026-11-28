namespace Notification.Application.Services.WalletTopups;

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Common.Extensions;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

public sealed class WalletTopupService(INotificationDbContext db, TimeProvider time) : IWalletTopupService
{
    public async Task<WalletTopupResponse> CreateAsync(WalletTopupRequest request, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        if (request is null || request.TopupId == Guid.Empty || request.TransactionId == Guid.Empty ||
            request.RecipientUserId == Guid.Empty || request.Amount is < 10000 or > 99999999 ||
            request.Amount != decimal.Truncate(request.Amount) || request.Currency != "VND" ||
            request.ConfirmedAt == default || request.ConfirmedAt > now.AddSeconds(30) ||
            request.SourceEventId != $"wallet-topup:{request.TopupId:D}:succeeded" ||
            request.TraceId?.Any(char.IsControl) == true || request.TraceId?.Length > 100)
            throw new DomainException("VAL_INVALID_INPUT", "Confirmed wallet top-up fields are invalid.", 400);

        var confirmedAt = request.ConfirmedAt.ToUniversalTime();
        var eventId = StableGuid(request.TopupId);
        // Trace context is excluded so a retry with a new trace ID remains the same event.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            request.TopupId, request.TransactionId, request.RecipientUserId, request.Amount,
            request.Currency, confirmedAt
        }))));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = $"wallet-topup:{request.TopupId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 8033))", cancellationToken);
        var existing = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(n => n.SourceEventId == eventId, cancellationToken);
        if (existing is not null)
        {
            if (existing.UserId != request.RecipientUserId ||
                existing.NotificationType != NotificationType.WalletTopupSucceeded ||
                !Matches(existing.Payload, hash))
                throw new DomainException("WALLET_TOPUP_CONFLICT", "Top-up was already notified with different data.", 409);
            return await Response(existing.Id, request.SourceEventId, true, cancellationToken);
        }

        var channels = await db.NotificationChannels.AsNoTracking()
            .Where(c => !c.IsDeleted && (c.Code == ChannelType.InApp || c.Code == ChannelType.Email))
            .ToListAsync(cancellationToken);
        if (channels.Count != 2)
            throw new DomainException("CHANNEL_NOT_FOUND", "In-app and email channel configurations are required.", 409);
        var inAppEnabled = !channels.Single(c => c.Code == ChannelType.InApp).IsDisabled;
        var emailEnabled = !channels.Single(c => c.Code == ChannelType.Email).IsDisabled;
        var amount = request.Amount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        var date = confirmedAt.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);
        const string title = "Nạp tiền vào ví thành công";
        var body = $"Ví của bạn đã được cộng {amount} VND lúc {date}. Mã giao dịch: {request.TransactionId:D}.";
        var html = $"""
            <!doctype html><html lang="vi"><body style="font-family:Arial,sans-serif;color:#17243b">
            <div style="max-width:560px;margin:auto;padding:24px;border:1px solid #dce4ee;border-radius:12px">
            <h2 style="color:#0877bd">{title}</h2>
            <p>Giao dịch nạp tiền vào ví Nexus của bạn đã hoàn tất.</p>
            <p><strong>Số tiền:</strong> {WebUtility.HtmlEncode(amount)} VND<br>
            <strong>Thời gian:</strong> {WebUtility.HtmlEncode(date)}<br>
            <strong>Mã giao dịch:</strong> {request.TransactionId:D}</p>
            <p>Bạn có thể kiểm tra số dư và lịch sử giao dịch trong ví.</p>
            <p style="font-size:12px;color:#607086">Nếu bạn không thực hiện giao dịch này, hãy liên hệ bộ phận hỗ trợ Nexus.</p>
            </div></body></html>
            """;
        var notification = new NotificationRecord
        {
            Id = Guid.NewGuid(), UserId = request.RecipientUserId, SourceEventId = eventId,
            NotificationType = NotificationType.WalletTopupSucceeded, ChannelScope = ChannelScope.All,
            Title = title, Body = body, IsInboxVisible = inAppEnabled, IsRead = !inAppEnabled,
            ReadAt = inAppEnabled ? null : now, Status = emailEnabled ? NotificationStatus.Pending : NotificationStatus.Completed,
            Payload = JsonSerializer.Serialize(new
            {
                type = NotificationType.WalletTopupSucceeded.ToString(), request.TopupId,
                request.TransactionId, request.Amount, request.Currency, confirmedAt,
                requestHash = hash, traceId = request.TraceId
            }), CreatedAt = now, UpdatedAt = now
        };
        db.Notifications.Add(notification);
        AddDelivery(notification.Id, ChannelType.InApp, request.RecipientUserId.ToString(),
            inAppEnabled ? DeliveryStatus.Sent : DeliveryStatus.Skipped, now);
        // Resolve the current, verified address from UserService when the email worker sends.
        AddDelivery(notification.Id, ChannelType.Email, $"user:{request.RecipientUserId:D}",
            emailEnabled ? DeliveryStatus.Pending : DeliveryStatus.Skipped, now,
            emailEnabled ? title : null, emailEnabled ? html : null);
        db.AddAuditLog(AuditAction.ServiceEventAccepted, nameof(NotificationRecord), notification.Id.ToString(),
            newValues: new { Source = "wallet", request.TopupId, request.TransactionId,
                request.RecipientUserId, request.Amount, request.Currency, request.SourceEventId },
            traceId: request.TraceId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await Response(notification.Id, request.SourceEventId, false, cancellationToken);
    }

    private void AddDelivery(Guid notificationId, ChannelType channel, string destination, DeliveryStatus status,
        DateTimeOffset now, string? subject = null, string? html = null)
    {
        var delivery = new NotificationDelivery
        {
            Id = Guid.NewGuid(), NotificationId = notificationId, ChannelCode = channel,
            Destination = destination, Status = status, SubjectOverride = subject, BodyOverride = html,
            AttemptCount = status == DeliveryStatus.Sent ? 1 : 0,
            LastAttemptAt = status == DeliveryStatus.Sent ? now : null,
            RowVersion = 1, CreatedAt = now, UpdatedAt = now
        };
        db.NotificationDeliveries.Add(delivery);
        db.NotificationLogs.Add(new NotificationLog
        {
            Id = Guid.NewGuid(), NotificationId = notificationId, DeliveryId = delivery.Id,
            Action = status == DeliveryStatus.Sent ? LogAction.Sent :
                status == DeliveryStatus.Skipped ? LogAction.Suppressed : LogAction.Created,
            Level = Notification.Domain.Enums.LogLevel.Info,
            Message = "Wallet top-up delivery recorded.",
            CreatedAt = now, UpdatedAt = now
        });
    }

    private async Task<WalletTopupResponse> Response(Guid notificationId, string sourceEventId,
        bool replay, CancellationToken cancellationToken)
    {
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.NotificationId == notificationId)
            .OrderBy(d => d.ChannelCode).ToListAsync(cancellationToken);
        if (deliveries.Count != 2 || deliveries.Count(d => d.ChannelCode == ChannelType.Email) != 1 ||
            deliveries.Count(d => d.ChannelCode == ChannelType.InApp) != 1)
            throw new DomainException("DELIVERY_STATE_INVALID", "Wallet top-up delivery state is invalid.", 409);
        return new WalletTopupResponse(sourceEventId, notificationId, replay,
            deliveries.Select(d => new WalletTopupDeliveryResponse(d.Id, d.ChannelCode.ToString(),
                d.Status.ToString())).ToList());
    }

    private static Guid StableGuid(Guid topupId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"wallet-topup:{topupId:D}"));
        var guidBytes = bytes[..16];
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static bool Matches(string? payload, string hash)
    {
        if (payload is null) return false;
        try
        {
            using var json = JsonDocument.Parse(payload);
            return json.RootElement.TryGetProperty("requestHash", out var value) &&
                value.GetString() == hash;
        }
        catch (JsonException) { return false; }
    }
}
