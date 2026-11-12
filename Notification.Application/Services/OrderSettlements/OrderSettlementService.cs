namespace Notification.Application.Services.OrderSettlements;

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

public sealed class OrderSettlementService(INotificationDbContext db, TimeProvider time) : IOrderSettlementService
{
    private static DateTimeOffset Utc(DateTimeOffset value) => new(value.ToUniversalTime().Ticks / 10 * 10, TimeSpan.Zero);
    private static bool Id(Guid? value) => value.HasValue && value != Guid.Empty;
    private Task<int> Lock(string key, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8026))", ct);
    private static DomainException Conflict() => new("ORDER_SETTLEMENT_CONFLICT", "Order settlement event conflicts with an existing request or dispatch.", 409);

    public async Task<OrderSettlementResponse> CreateAsync(CreateOrderSettlementRequest r, CancellationToken cancellationToken = default)
    {
        var auction = string.Equals(r?.OrderKind, "Auction", StringComparison.OrdinalIgnoreCase);
        var regular = string.Equals(r?.OrderKind, "Regular", StringComparison.OrdinalIgnoreCase);
        var now = Utc(time.GetUtcNow());
        if (r is null || !string.Equals(r.OrderStatus, "Completed", StringComparison.OrdinalIgnoreCase) || !Id(r.SourceEventId) || !Id(r.SettlementId) || !Id(r.WalletCreditTransactionId) || !Id(r.OrderId) || !Id(r.SellerUserId) ||
            (!auction && !regular) || (auction && !Id(r.AuctionId)) || (regular && r.AuctionId.HasValue) ||
            string.IsNullOrWhiteSpace(r.OrderNo) || r.OrderNo.Length > 100 || r.OrderNo.Any(char.IsControl) || r.NetAmount is null or <= 0 or > 9999999999999999.99m ||
            decimal.Round(r.NetAmount.Value, 2) != r.NetAmount || !string.Equals(r.Currency, "VND", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(r.SettlementStatus, "Completed", StringComparison.OrdinalIgnoreCase) || !string.Equals(r.WalletCreditStatus, "Completed", StringComparison.OrdinalIgnoreCase) || !string.Equals(r.SettlementDestination, "SellerWallet", StringComparison.OrdinalIgnoreCase) ||
            r.OrderCompletedAt is null || r.WalletCreditedAt is null || r.SettledAt is null || Utc(r.WalletCreditedAt.Value) < Utc(r.OrderCompletedAt.Value) ||
            Utc(r.SettledAt.Value) < Utc(r.WalletCreditedAt.Value) || Utc(r.SettledAt.Value) > now.AddSeconds(30) || r.TraceId?.Length > 100 || r.TraceId?.Any(char.IsControl) == true)
            throw new DomainException("VAL_INVALID_INPUT", "Order settlement fields or confirmed wallet credit are invalid.");
        var eventId = r.SourceEventId!.Value; var settlement = r.SettlementId!.Value; var credit = r.WalletCreditTransactionId!.Value; var order = r.OrderId!.Value; var target = r.SellerUserId!.Value;
        var kind = auction ? NotificationOrderKind.Auction : NotificationOrderKind.Regular; var orderNo = r.OrderNo.Trim();
        var orderCompleted = Utc(r.OrderCompletedAt.Value); var credited = Utc(r.WalletCreditedAt.Value); var settled = Utc(r.SettledAt.Value);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { settlement, credit, order, target, kind, r.AuctionId, orderNo, OrderStatus = "Completed", Amount = r.NetAmount.Value.ToString("0.00", CultureInfo.InvariantCulture), Currency = "VND", SettlementStatus = "Completed", WalletCreditStatus = "Completed", SettlementDestination = "SellerWallet", orderCompleted, credited, settled }))));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await Lock("order-settlement-event:" + eventId.ToString("N"), cancellationToken);
        var old = await db.OrderSettlementDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (old is not null) { if (old.RequestHash != hash) throw Conflict(); return await Response(old, true, cancellationToken); }
        await Lock("order-settlement-settlement:" + settlement.ToString("N"), cancellationToken);
        await Lock("order-settlement-credit:" + credit.ToString("N"), cancellationToken);
        await Lock("order-settlement-order:" + kind + ":" + order.ToString("N"), cancellationToken);
        if (r.AuctionId.HasValue) await Lock("order-settlement-auction:" + r.AuctionId.Value.ToString("N"), cancellationToken);
        if (await db.OrderSettlementDispatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.SettlementId == settlement || x.WalletCreditTransactionId == credit || (x.OrderKind == kind && x.OrderId == order) || (r.AuctionId.HasValue && x.AuctionId == r.AuctionId), cancellationToken))
            throw new DomainException("ORDER_SETTLEMENT_ALREADY_NOTIFIED", "Settlement, wallet credit or order already has its final settlement notification.", 409);
        var code = "ORDER_SELLER_SETTLED_PUSH"; var locale = "vi-VN";
        var template = await db.NotificationTemplates.FromSqlInterpolated(
            $"SELECT * FROM \"NotificationTemplates\" WHERE \"Code\" = {code} AND \"Locale\" = {locale} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(t => t.IsActive, cancellationToken);
        if (template is null) throw new DomainException("TEMPLATE_NOT_FOUND", "Active order settlement template was not found.", 404);
        if (template.ChannelCode != ChannelType.Push || template.NotificationType != NotificationType.OrderSellerSettled || !template.IsMandatory)
            throw new DomainException("ORDER_SETTLEMENT_TEMPLATE_INVALID", "Order settlement template must be mandatory order seller settled push.", 409);
        var channels = await db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted && (c.Code == ChannelType.InApp || c.Code == ChannelType.Push)).ToListAsync(cancellationToken);
        if (channels.Count != 2) throw new DomainException("CHANNEL_NOT_FOUND", "Push and in-app channel configurations are required.", 409);
        var mandatory = await NotificationPreferencePolicy.MandatoryPairsAsync(db, cancellationToken);
        var preferences = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == target && p.NotificationType == NotificationType.OrderSellerSettled && !p.IsDeleted && p.IsDisabled)
            .Select(p => p.ChannelCode).ToListAsync(cancellationToken);
        string? Policy(ChannelType channel) => channels.Single(c => c.Code == channel).IsDisabled ? "CHANNEL_DISABLED" :
            !mandatory.Contains((NotificationType.OrderSellerSettled, channel)) && preferences.Contains(channel) ? "PREFERENCE_DISABLED" : null;
        var inAppError = Policy(ChannelType.InApp); var pushError = Policy(ChannelType.Push);
        var devices = pushError is not null ? new List<Guid>() : await db.DeviceTokens.AsNoTracking().Where(d => d.UserId == target && d.IsActive && !d.IsDeleted)
            .OrderBy(d => d.Id).Select(d => d.Id).Take(101).ToListAsync(cancellationToken);
        pushError ??= devices.Count == 0 ? "PUSH_NO_ACTIVE_DEVICES" : devices.Count > 100 ? "PUSH_DEVICE_LIMIT" : null;
        var visible = inAppError is null; var hidden = !visible && pushError is not null;
        var data = new Dictionary<string, string> { ["OrderNo"] = orderNo, ["NetAmount"] = r.NetAmount.Value.ToString("N2", CultureInfo.InvariantCulture), ["SettledAtUtc"] = settled.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) };
        var row = new NotificationRecord
        {
            Id = Guid.NewGuid(),
            UserId = target,
            SourceEventId = eventId,
            TemplateId = template.Id,
            NotificationType = NotificationType.OrderSellerSettled,
            ChannelScope = ChannelScope.All,
            Title = TemplateRenderer.Render(template.SubjectTemplate, data, false, 500),
            Body = TemplateRenderer.Render(template.BodyTemplate, data, false, 4000),
            Payload = JsonSerializer.Serialize(new
            {
                type = "OrderSellerSettled",
                audience = "Seller",
                orderKind = kind.ToString(),
                orderId = order,
                auctionId = r.AuctionId,
                orderNo,
                settlementId = settlement,
                walletCreditTransactionId = credit,
                netAmount = r.NetAmount,
                currency = "VND",
                orderCompletedAt = orderCompleted,
                walletCreditedAt = credited,
                settledAt = settled,
                orderStatus = "Completed", settlementStatus = "Completed",
                walletCreditStatus = "Completed",
                settlementDestination = "SellerWallet"
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
        var receipt = new OrderSettlementDispatch { Id = eventId, SettlementId = settlement, WalletCreditTransactionId = credit, OrderId = order, OrderKind = kind, AuctionId = r.AuctionId, NotificationId = row.Id, RequestHash = hash, CreatedAt = now, UpdatedAt = now };
        db.OrderSettlementDispatches.Add(receipt);
        db.AddAuditLog(AuditAction.OrderSettlementAccepted, nameof(OrderSettlementDispatch), eventId.ToString(), newValues: new
        { SourceEventId = eventId, SettlementId = settlement, WalletCreditTransactionId = credit, OrderId = order, OrderKind = kind.ToString(), SellerUserId = target, NotificationId = row.Id }, traceId: r.TraceId);
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
            Message = "Order settlement delivery recorded.",
            Metadata = JsonSerializer.Serialize(new { d.ErrorCode }),
            CreatedAt = now,
            UpdatedAt = now
        });
    }
    private async Task<OrderSettlementResponse> Response(OrderSettlementDispatch r, bool replay, CancellationToken ct)
    {
        var row = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.NotificationId, ct);
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().Where(x => x.NotificationId == r.NotificationId).OrderBy(x => x.ChannelCode).ThenBy(x => x.Id).ToListAsync(ct);
        if (row is null || deliveries.Count == 0) throw new DomainException("DELIVERY_STATE_INVALID", "Order settlement delivery state is invalid.", 409);
        return new(r.Id, r.SettlementId, r.OrderId, row.Id, row.UserId, row.Status.ToString(), row.IsInboxVisible && !row.IsDeleted, replay,
            deliveries.Select(d => new OrderSettlementDeliveryResponse(d.Id, d.ChannelCode.ToString(), d.Status.ToString(), d.ErrorCode)).ToList());
    }
    public async Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var row = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId && !n.IsDeleted &&
            db.OrderSettlementDispatches.IgnoreQueryFilters().Any(r => r.NotificationId == n.Id), cancellationToken);
        if (row is null) throw new DomainException("NOTIFICATION_NOT_FOUND", "Order settlement notification was not found or does not belong to the user.", 404);
        return new(row.Id, row.UserId, row.Title, row.Body, row.Payload, row.IsRead, row.ReadAt, row.NotificationType.ToString(), row.Status.ToString(), row.CreatedAt);
    }
}
