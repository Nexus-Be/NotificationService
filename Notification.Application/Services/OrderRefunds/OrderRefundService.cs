namespace Notification.Application.Services.OrderRefunds;

using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Common.Extensions;
using Notification.Application.Interfaces;
using Notification.Application.Services.Dispatches;
using Notification.Application.Services.Notifications;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

public sealed class OrderRefundService(INotificationDbContext db, TimeProvider time) : IOrderRefundService
{
    private static DateTimeOffset Utc(DateTimeOffset value) 
        => new(value.ToUniversalTime().Ticks / 10 * 10, TimeSpan.Zero);

    private static bool Id(Guid? value) 
        => value.HasValue && value != Guid.Empty;

    private Task<int> Lock(string key, CancellationToken ct) 
        => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8023))", ct);

    private static DomainException Conflict() 
        => new("ORDER_REFUND_CONFLICT", "Order refund event conflicts with an existing request or dispatch.", 409);

    public async Task<OrderRefundResponse> CreateAsync(CreateOrderRefundRequest r, CancellationToken cancellationToken = default)
    {
        var auction = string.Equals(r?.OrderKind, "Auction", StringComparison.OrdinalIgnoreCase);
        var regular = string.Equals(r?.OrderKind, "Regular", StringComparison.OrdinalIgnoreCase);
        var now = Utc(time.GetUtcNow());

        if (r is null || !Id(r.SourceEventId) || !Id(r.RefundId) || !Id(r.WalletCreditTransactionId) || !Id(r.OrderId) || !Id(r.BuyerUserId) ||
            (!auction && !regular) || (auction && !Id(r.AuctionId)) || (regular && r.AuctionId.HasValue) ||
            string.IsNullOrWhiteSpace(r.OrderNo) || r.OrderNo.Length > 100 || r.OrderNo.Any(char.IsControl) || r.RefundAmount is null or <= 0 or > 9999999999999999.99m ||
            decimal.Round(r.RefundAmount.Value, 2) != r.RefundAmount || !string.Equals(r.Currency, "VND", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(r.RefundStatus, "Completed", StringComparison.OrdinalIgnoreCase) || !string.Equals(r.WalletCreditStatus, "Completed", StringComparison.OrdinalIgnoreCase) || !string.Equals(r.RefundDestination, "BuyerWallet", StringComparison.OrdinalIgnoreCase) ||
            r.RequestedAt is null || r.WalletCreditedAt is null || r.CompletedAt is null || Utc(r.WalletCreditedAt.Value) < Utc(r.RequestedAt.Value) ||
            Utc(r.CompletedAt.Value) < Utc(r.WalletCreditedAt.Value) || Utc(r.CompletedAt.Value) > now.AddSeconds(30) || r.TraceId?.Length > 100 || r.TraceId?.Any(char.IsControl) == true)
            throw new DomainException("VAL_INVALID_INPUT", "Order refund fields or confirmed wallet credit are invalid.");

        var eventId = r.SourceEventId!.Value; var refund = r.RefundId!.Value; var credit = r.WalletCreditTransactionId!.Value; var order = r.OrderId!.Value; var target = r.BuyerUserId!.Value;
        var kind = auction ? NotificationOrderKind.Auction : NotificationOrderKind.Regular; var orderNo = r.OrderNo.Trim();
        var requested = Utc(r.RequestedAt.Value); var credited = Utc(r.WalletCreditedAt.Value); var completed = Utc(r.CompletedAt.Value);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { refund, credit, order, target, kind, r.AuctionId, orderNo, Amount = r.RefundAmount.Value.ToString("0.00", CultureInfo.InvariantCulture), Currency = "VND", RefundStatus = "Completed", WalletCreditStatus = "Completed", RefundDestination = "BuyerWallet", requested, credited, completed }))));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await Lock("order-refund-event:" + eventId.ToString("N"), cancellationToken);
        var old = await db.OrderRefundDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (old is not null) { if (old.RequestHash != hash) throw Conflict(); return await Response(old, true, cancellationToken); }
        await Lock("order-refund-refund:" + refund.ToString("N"), cancellationToken);
        await Lock("order-refund-credit:" + credit.ToString("N"), cancellationToken);
        if (await db.OrderRefundDispatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.RefundId == refund || x.WalletCreditTransactionId == credit, cancellationToken))
            throw new DomainException("ORDER_REFUND_ALREADY_NOTIFIED", "Refund or wallet credit already has its completion notification.", 409);
        var code = "ORDER_REFUND_COMPLETED_PUSH"; var locale = "vi-VN";
        var template = await db.NotificationTemplates.FromSqlInterpolated(
            $"SELECT * FROM \"NotificationTemplates\" WHERE \"Code\" = {code} AND \"Locale\" = {locale} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(t => t.IsActive, cancellationToken);
        if (template is null) throw new DomainException("TEMPLATE_NOT_FOUND", "Active order refund template was not found.", 404);
        if (template.ChannelCode != ChannelType.Push || template.NotificationType != NotificationType.OrderRefunded || !template.IsMandatory)
            throw new DomainException("ORDER_REFUND_TEMPLATE_INVALID", "Order refund template must be mandatory order refunded push.", 409);
        var channels = await db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted && (c.Code == ChannelType.InApp || c.Code == ChannelType.Push)).ToListAsync(cancellationToken);
        if (channels.Count != 2) throw new DomainException("CHANNEL_NOT_FOUND", "Push and in-app channel configurations are required.", 409);
        var mandatory = await NotificationPreferencePolicy.MandatoryPairsAsync(db, cancellationToken);
        var preferences = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == target && p.NotificationType == NotificationType.OrderRefunded && !p.IsDeleted && p.IsDisabled)
            .Select(p => p.ChannelCode).ToListAsync(cancellationToken);
        string? Policy(ChannelType channel) => channels.Single(c => c.Code == channel).IsDisabled ? "CHANNEL_DISABLED" :
            !mandatory.Contains((NotificationType.OrderRefunded, channel)) && preferences.Contains(channel) ? "PREFERENCE_DISABLED" : null;
        var inAppError = Policy(ChannelType.InApp); var pushError = Policy(ChannelType.Push);
        var devices = pushError is not null ? new List<Guid>() : await db.DeviceTokens.AsNoTracking().Where(d => d.UserId == target && d.IsActive && !d.IsDeleted)
            .OrderBy(d => d.Id).Select(d => d.Id).Take(101).ToListAsync(cancellationToken);
        pushError ??= devices.Count == 0 ? "PUSH_NO_ACTIVE_DEVICES" : devices.Count > 100 ? "PUSH_DEVICE_LIMIT" : null;
        var visible = inAppError is null; var hidden = !visible && pushError is not null;
        var data = new Dictionary<string, string> { ["OrderNo"] = orderNo, ["RefundAmount"] = r.RefundAmount.Value.ToString("N2", CultureInfo.InvariantCulture), ["CompletedAtUtc"] = completed.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) };
        var row = new NotificationRecord
        {
            Id = Guid.NewGuid(),
            UserId = target,
            SourceEventId = eventId,
            TemplateId = template.Id,
            NotificationType = NotificationType.OrderRefunded,
            ChannelScope = ChannelScope.All,
            Title = TemplateRenderer.Render(template.SubjectTemplate, data, false, 500),
            Body = TemplateRenderer.Render(template.BodyTemplate, data, false, 4000),
            Payload = JsonSerializer.Serialize(new
            {
                type = "OrderRefunded",
                audience = "Buyer",
                orderKind = kind.ToString(),
                orderId = order,
                auctionId = r.AuctionId,
                orderNo,
                refundId = refund,
                walletCreditTransactionId = credit,
                refundAmount = r.RefundAmount,
                currency = "VND",
                requestedAt = requested,
                walletCreditedAt = credited,
                completedAt = completed,
                refundStatus = "Completed",
                walletCreditStatus = "Completed",
                refundDestination = "BuyerWallet"
            }),
            IsInboxVisible = visible,
            IsRead = !visible,
            ReadAt = visible ? null : now,
            IsDeleted = hidden,
            DeletedAt = hidden ? now : null,
            Status = pushError is null ? NotificationStatus.Pending : NotificationStatus.Completed,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Notifications.Add(row);
        AddDelivery(row, ChannelType.InApp, target.ToString(), visible ? DeliveryStatus.Sent : DeliveryStatus.Skipped, inAppError, now);
        if (pushError is not null) AddDelivery(row, ChannelType.Push, target.ToString(), DeliveryStatus.Skipped, pushError, now);
        else foreach (var device in devices) AddDelivery(row, ChannelType.Push, device.ToString(), DeliveryStatus.Pending, null, now);
        var receipt = new OrderRefundDispatch { Id = eventId, RefundId = refund, WalletCreditTransactionId = credit, OrderId = order, OrderKind = kind, AuctionId = r.AuctionId, NotificationId = row.Id, RequestHash = hash, CreatedAt = now, UpdatedAt = now };
        db.OrderRefundDispatches.Add(receipt);
        db.AddAuditLog(AuditAction.OrderRefundAccepted, nameof(OrderRefundDispatch), eventId.ToString(), newValues: new
        { SourceEventId = eventId, RefundId = refund, WalletCreditTransactionId = credit, OrderId = order, OrderKind = kind.ToString(), BuyerUserId = target, NotificationId = row.Id }, traceId: r.TraceId);
        try { await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" }) { throw Conflict(); }
        return await Response(receipt, false, cancellationToken);
    }
    private void AddDelivery(NotificationRecord parent, ChannelType channel, string destination, DeliveryStatus status, string? error, DateTimeOffset now)
    {
        var d = new NotificationDelivery
        {
            Id = Guid.NewGuid(),
            NotificationId = parent.Id,
            ChannelCode = channel,
            Destination = destination,
            Status = status,
            AttemptCount = status == DeliveryStatus.Sent ? 1 : 0,
            LastAttemptAt = status == DeliveryStatus.Sent ? now : null,
            ErrorCode = error,
            RowVersion = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.NotificationDeliveries.Add(d);
        db.NotificationLogs.Add(new NotificationLog
        {
            Id = Guid.NewGuid(),
            NotificationId = parent.Id,
            DeliveryId = d.Id,
            Action = status == DeliveryStatus.Sent ? LogAction.Sent : status == DeliveryStatus.Skipped ? LogAction.Suppressed : LogAction.Created,
            Level = Notification.Domain.Enums.LogLevel.Info,
            Message = "Order refund delivery recorded.",
            Metadata = JsonSerializer.Serialize(new { d.ErrorCode }),
            CreatedAt = now,
            UpdatedAt = now
        });
    }
    private async Task<OrderRefundResponse> Response(OrderRefundDispatch r, bool replay, CancellationToken ct)
    {
        var row = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.NotificationId, ct);
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().Where(x => x.NotificationId == r.NotificationId).OrderBy(x => x.ChannelCode).ThenBy(x => x.Id).ToListAsync(ct);

        if (row is null || deliveries.Count == 0) throw new DomainException("DELIVERY_STATE_INVALID", "Order refund delivery state is invalid.", 409);
        return new(r.Id, r.RefundId, r.OrderId, row.Id, row.UserId, row.Status.ToString(), row.IsInboxVisible && !row.IsDeleted, replay,
            deliveries.Select(d => new OrderRefundDeliveryResponse(d.Id, d.ChannelCode.ToString(), d.Status.ToString(), d.ErrorCode)).ToList());
    }
    public async Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var row = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId && !n.IsDeleted &&
            db.OrderRefundDispatches.IgnoreQueryFilters().Any(r => r.NotificationId == n.Id), cancellationToken);
        if (row is null) throw new DomainException("NOTIFICATION_NOT_FOUND", "Order refund notification was not found or does not belong to the user.", 404);
        return new(row.Id, row.UserId, row.Title, row.Body, row.Payload, row.IsRead, row.ReadAt, row.NotificationType.ToString(), row.Status.ToString(), row.CreatedAt);
    }
}
