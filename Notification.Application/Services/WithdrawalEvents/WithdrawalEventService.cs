namespace Notification.Application.Services.WithdrawalEvents;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Common.Extensions;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

/// <summary>
/// Creates the in-app notice for a confirmed withdrawal result. A repeated source event returns the stored notification instead of a second one;
/// the same source event with different content is a conflict.
/// </summary>
public sealed class WithdrawalEventService(INotificationDbContext db, TimeProvider time) : IWithdrawalEventService
{
    private static readonly string[] Statuses = ["Approved", "Rejected", "Completed", "Failed"];

    public async Task<WithdrawalEventResponse> CreateAsync(WithdrawalEventRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SourceEventId) || request.SourceEventId.Trim().Length > 255 ||
            request.SourceEventId.Any(char.IsControl) || request.WithdrawalId == Guid.Empty || request.RecipientUserId == Guid.Empty ||
            request.ConfirmedAt == default || !Statuses.Contains(request.Status, StringComparer.Ordinal) ||
            (request.DisplayReason is not null && (request.DisplayReason.Length > 500 || request.DisplayReason.Any(char.IsControl))) ||
            (request.TransactionId is not null && (request.TransactionId.Length > 100 || request.TransactionId.Any(char.IsControl))) ||
            (request.TraceId is not null && (request.TraceId.Length > 100 || request.TraceId.Any(char.IsControl))))
            throw new DomainException("VAL_INVALID_INPUT", "Withdrawal event fields are invalid.", 400);

        var sourceEventKey = request.SourceEventId.Trim();
        var sourceEventId = StableGuid(sourceEventKey);
        var confirmedAt = request.ConfirmedAt.ToUniversalTime();
        // Trace context is excluded so a retry with a new trace id is still a replay.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            sourceEventKey, request.WithdrawalId, request.RecipientUserId, request.Status, confirmedAt,
            request.DisplayReason, request.TransactionId
        }))));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lockKey = $"withdrawal-event:{sourceEventId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 8031))", ct);

        var existing = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.SourceEventId == sourceEventId).ToListAsync(ct);
        if (existing.Count > 0)
        {
            var replay = existing.Count == 1 ? existing[0] : null;
            if (replay is null || replay.UserId != request.RecipientUserId ||
                replay.NotificationType != NotificationType.WithdrawalResult || !Matches(replay.Payload, hash))
                throw new DomainException("WITHDRAWAL_EVENT_CONFLICT", "Source event has different notification data.", 409);
            return new WithdrawalEventResponse(sourceEventKey, true, replay.Id);
        }

        var (title, body) = Describe(request.Status, request.DisplayReason);
        var channel = await db.NotificationChannels.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == ChannelType.InApp && !c.IsDeleted, ct)
            ?? throw new DomainException("CHANNEL_NOT_FOUND", "In-app channel was not found.", 409);
        var optedOut = await db.NotificationPreferences.AsNoTracking().AnyAsync(p =>
            p.UserId == request.RecipientUserId && p.NotificationType == NotificationType.WithdrawalResult &&
            p.ChannelCode == ChannelType.InApp && p.IsDisabled && !p.IsDeleted, ct);
        var visible = !channel.IsDisabled && !optedOut;
        var now = time.GetUtcNow();

        var row = new NotificationRecord
        {
            Id = Guid.NewGuid(), UserId = request.RecipientUserId, SourceEventId = sourceEventId,
            NotificationType = NotificationType.WithdrawalResult, ChannelScope = ChannelScope.Specific,
            Title = title, Body = body, Status = NotificationStatus.Completed,
            IsInboxVisible = visible, IsRead = !visible,
            Payload = JsonSerializer.Serialize(new
            {
                type = NotificationType.WithdrawalResult.ToString(), withdrawalId = request.WithdrawalId, status = request.Status,
                confirmedAt, transactionId = request.TransactionId, requestHash = hash, traceId = request.TraceId
            }),
            CreatedAt = now, UpdatedAt = now
        };
        db.Notifications.Add(row);
        db.NotificationDeliveries.Add(new NotificationDelivery
        {
            Id = Guid.NewGuid(), NotificationId = row.Id, ChannelCode = ChannelType.InApp,
            Destination = request.RecipientUserId.ToString(), Status = visible ? DeliveryStatus.Sent : DeliveryStatus.Skipped,
            AttemptCount = 1, LastAttemptAt = now, RowVersion = 1, CreatedAt = now, UpdatedAt = now
        });
        db.AddAuditLog(AuditAction.ServiceEventAccepted, nameof(NotificationRecord), row.Id.ToString(),
            newValues: new { Source = "wallet", SourceEventId = sourceEventKey, Type = NotificationType.WithdrawalResult.ToString(), Visible = visible },
            traceId: request.TraceId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new WithdrawalEventResponse(sourceEventKey, false, row.Id);
    }

    private static (string Title, string Body) Describe(string status, string? reason) => status switch
    {
        "Approved" => ("Yêu cầu rút tiền đã được duyệt", "Yêu cầu rút tiền của bạn đã được duyệt và đang chờ chuyển khoản."),
        "Completed" => ("Rút tiền thành công", "Tiền rút đã được chuyển cho bạn."),
        "Rejected" => ("Yêu cầu rút tiền bị từ chối", string.IsNullOrWhiteSpace(reason)
            ? "Yêu cầu rút tiền của bạn đã bị từ chối. Số tiền đã được hoàn về số dư khả dụng."
            : $"Yêu cầu rút tiền của bạn đã bị từ chối: {reason.Trim()}. Số tiền đã được hoàn về số dư khả dụng."),
        "Failed" => ("Rút tiền thất bại", string.IsNullOrWhiteSpace(reason)
            ? "Việc chuyển tiền không thành công. Số tiền đã được hoàn về số dư khả dụng."
            : $"Việc chuyển tiền không thành công: {reason.Trim()}. Số tiền đã được hoàn về số dư khả dụng."),
        _ => throw new DomainException("VAL_INVALID_INPUT", "Withdrawal event fields are invalid.", 400)
    };

    /// <summary>Derives a deterministic id from the string source event so replays find the stored notification.</summary>
    private static Guid StableGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("withdrawal-event:" + value));
        var guidBytes = bytes[..16];
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static bool Matches(string? payload, string hash)
    {
        if (payload is null) return false;
        using var json = JsonDocument.Parse(payload);
        return json.RootElement.TryGetProperty("requestHash", out var stored) && stored.GetString() == hash;
    }
}
