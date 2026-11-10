namespace Notification.Application.Services.OrderBuyerCancellations;

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

public sealed class OrderBuyerCancellationService(INotificationDbContext db, TimeProvider time) : IOrderBuyerCancellationService
{
    private static DateTimeOffset Utc(DateTimeOffset v) => new(v.ToUniversalTime().Ticks / 10 * 10, TimeSpan.Zero);
    private Task<int> Lock(string key, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8028))", ct);
    private static bool Id(Guid? value) => value.HasValue && value != Guid.Empty;
    public async Task<OrderBuyerCancellationResponse> CreateAsync(CreateOrderBuyerCancellationRequest r, CancellationToken cancellationToken = default)
    {
        var now = Utc(time.GetUtcNow());
        if (r is null || !Id(r.SourceEventId) || !Id(r.OrderId) || !Id(r.ShopId) || !Id(r.BuyerUserId) || !Id(r.SellerUserId) || r.BuyerUserId == r.SellerUserId ||
            !string.Equals(r.OrderKind, "Regular", StringComparison.OrdinalIgnoreCase) ||
            !(string.Equals(r.PreviousOrderStatus, "Paid", StringComparison.OrdinalIgnoreCase) || string.Equals(r.PreviousOrderStatus, "Shipping", StringComparison.OrdinalIgnoreCase)) ||
            !string.Equals(r.OrderStatus, "Cancelled", StringComparison.OrdinalIgnoreCase) || !string.Equals(r.CancelledBy, "Buyer", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(r.HandoverStatus, "NotHandedOver", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(r.OrderNo) || r.OrderNo.Length > 100 || r.OrderNo.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 500 || r.Reason.Any(char.IsControl) ||
            r.OrderCreatedAt is null || r.PaidAt is null || r.CancelledAt is null || Utc(r.PaidAt.Value) < Utc(r.OrderCreatedAt.Value) ||
            Utc(r.CancelledAt.Value) < Utc(r.PaidAt.Value) || Utc(r.CancelledAt.Value) > now.AddSeconds(30) || r.TraceId?.Length > 100 || r.TraceId?.Any(char.IsControl) == true)
            throw new DomainException("VAL_INVALID_INPUT", "Paid regular order buyer cancellation fields, confirmed state or source clock are invalid.");
        r = r with
        {
            OrderKind = "Regular",
            PreviousOrderStatus = string.Equals(r.PreviousOrderStatus, "Paid", StringComparison.OrdinalIgnoreCase) ? "Paid" : "Shipping",
            OrderStatus = "Cancelled",
            CancelledBy = "Buyer",
            HandoverStatus = "NotHandedOver",
            OrderNo = r.OrderNo.Trim(),
            Reason = r.Reason.Trim(),
            OrderCreatedAt = Utc(r.OrderCreatedAt.Value),
            PaidAt = Utc(r.PaidAt.Value),
            CancelledAt = Utc(r.CancelledAt.Value)
        };
        var id = r.SourceEventId!.Value; var order = r.OrderId!.Value;
        var proof = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { r.OrderKind, order, r.ShopId, r.BuyerUserId, r.SellerUserId, r.OrderNo, r.PreviousOrderStatus, r.OrderStatus, r.CancelledBy, r.HandoverStatus, r.Reason, r.OrderCreatedAt, r.PaidAt, r.CancelledAt }))));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await Lock("order-buyer-cancellation-event:" + id.ToString("N"), cancellationToken);
        var existing = await db.OrderBuyerCancellationDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is not null) { if (existing.RequestHash != proof) throw Conflict(); return await Response(existing, true, cancellationToken); }
        await Lock("order-buyer-cancellation-order:" + order.ToString("N"), cancellationToken);
        if (await db.OrderBuyerCancellationDispatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.OrderId == order, cancellationToken))
            throw new DomainException("ORDER_BUYER_CANCELLATION_ALREADY_NOTIFIED", "Order already has its buyer cancellation notifications.", 409);
        var codes = new[] { "ORDER_BUYER_CANCELLED_BUYER_PUSH", "ORDER_BUYER_CANCELLED_SELLER_PUSH" }; var locale = "vi-VN";
        var templates = await db.NotificationTemplates.FromSqlInterpolated(
            $"SELECT * FROM \"NotificationTemplates\" WHERE \"Code\" = ANY ({codes}) AND \"Locale\" = {locale} AND \"IsActive\" = TRUE AND \"IsDeleted\" = FALSE ORDER BY \"Id\" FOR SHARE")
            .AsNoTracking().ToListAsync(cancellationToken);
        if (templates.Count != 2) throw new DomainException("TEMPLATE_NOT_FOUND", "Active order buyer cancellation templates were not found.", 404);
        if (templates.Any(t => t.ChannelCode != ChannelType.Push || t.NotificationType != NotificationType.OrderBuyerCancelled || !t.IsMandatory))
            throw new DomainException("ORDER_BUYER_CANCELLATION_TEMPLATE_INVALID", "Order buyer cancellation templates must be mandatory order buyer cancelled push templates.", 409);
        var channels = await db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted && (c.Code == ChannelType.Push || c.Code == ChannelType.InApp)).ToListAsync(cancellationToken);
        if (channels.Count != 2) throw new DomainException("CHANNEL_NOT_FOUND", "Push and in-app channel configurations are required.", 409);
        var data = new Dictionary<string, string> { ["OrderNo"] = r.OrderNo, ["Reason"] = r.Reason, ["CancelledAtUtc"] = r.CancelledAt.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) };
        var buyer = await Branch(r.BuyerUserId!.Value, "Buyer", templates.Single(t => t.Code == codes[0]), r, data, channels, now, cancellationToken);
        var seller = await Branch(r.SellerUserId!.Value, "Seller", templates.Single(t => t.Code == codes[1]), r, data, channels, now, cancellationToken);
        var receipt = new OrderBuyerCancellationDispatch { Id = id, OrderId = order, RequestHash = proof, BuyerNotificationId = buyer.Id, SellerNotificationId = seller.Id, CreatedAt = now, UpdatedAt = now };
        db.OrderBuyerCancellationDispatches.Add(receipt);
        db.AddAuditLog(AuditAction.OrderBuyerCancellationAccepted, nameof(OrderBuyerCancellationDispatch), id.ToString(), newValues: new
        { SourceEventId = id, OrderId = order, r.BuyerUserId, r.SellerUserId, BuyerNotificationId = buyer.Id, SellerNotificationId = seller.Id }, traceId: r.TraceId);
        try { await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" }) { throw Conflict(); }
        return await Response(receipt, false, cancellationToken);
    }
    private async Task<NotificationRecord> Branch(Guid user, string audience, NotificationTemplate template, CreateOrderBuyerCancellationRequest request, Dictionary<string, string> data, List<NotificationChannel> channels, DateTimeOffset now, CancellationToken ct)
    {
        var mandatory = await NotificationPreferencePolicy.MandatoryPairsAsync(db, ct);
        var disabled = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == user && p.NotificationType == NotificationType.OrderBuyerCancelled && p.IsDisabled && !p.IsDeleted).Select(p => p.ChannelCode).ToListAsync(ct);
        string? Policy(ChannelType channel) => channels.Single(c => c.Code == channel).IsDisabled ? "CHANNEL_DISABLED" : !mandatory.Contains((NotificationType.OrderBuyerCancelled, channel)) && disabled.Contains(channel) ? "PREFERENCE_DISABLED" : null;
        var inboxError = Policy(ChannelType.InApp); var inbox = inboxError is null; var pushPolicy = Policy(ChannelType.Push); var pushDisabled = pushPolicy is not null;
        var devices = pushDisabled ? new List<Guid>() : await db.DeviceTokens.AsNoTracking().Where(d => d.UserId == user && d.IsActive && !d.IsDeleted)
            .OrderBy(d => d.Id).Select(d => d.Id).Take(101).ToListAsync(ct);
        var pushError = pushDisabled ? pushPolicy : devices.Count == 0 ? "PUSH_NO_ACTIVE_DEVICES" : devices.Count > 100 ? "PUSH_DEVICE_LIMIT" : null;
        var payload = new
        {
            type = "OrderBuyerCancelled",
            audience,
            orderKind = request.OrderKind,
            orderId = request.OrderId,
            shopId = request.ShopId,
            orderNo = request.OrderNo,
            previousOrderStatus = request.PreviousOrderStatus,
            orderStatus = request.OrderStatus,
            cancelledBy = request.CancelledBy,
            handoverStatus = request.HandoverStatus,
            orderCreatedAt = request.OrderCreatedAt,
            paidAt = request.PaidAt,
            cancelledAt = request.CancelledAt,
            reason = request.Reason
        };
        var row = new NotificationRecord
        {
            Id = Guid.NewGuid(),
            UserId = user,
            SourceEventId = request.SourceEventId,
            TemplateId = template.Id,
            NotificationType = NotificationType.OrderBuyerCancelled,
            ChannelScope = ChannelScope.All,
            Title = TemplateRenderer.Render(template.SubjectTemplate, data, false, 500),
            Body = TemplateRenderer.Render(template.BodyTemplate, data, false, 4000),
            Payload = JsonSerializer.Serialize(payload),
            IsInboxVisible = inbox,
            IsRead = !inbox,
            ReadAt = inbox ? null : now,
            IsDeleted = !inbox && pushError is not null,
            DeletedAt = !inbox && pushError is not null ? now : null,
            Status = pushError is null ? NotificationStatus.Pending : NotificationStatus.Completed,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Notifications.Add(row);
        AddDelivery(row, ChannelType.InApp, user.ToString(), inbox ? DeliveryStatus.Sent : DeliveryStatus.Skipped, inboxError, now);
        if (pushError is not null) AddDelivery(row, ChannelType.Push, user.ToString(), DeliveryStatus.Skipped, pushError, now);
        else foreach (var device in devices) AddDelivery(row, ChannelType.Push, device.ToString(), DeliveryStatus.Pending, null, now);
        return row;
    }
    private void AddDelivery(NotificationRecord parent, ChannelType channel, string destination, DeliveryStatus status, string? error, DateTimeOffset now)
    {
        var delivery = new NotificationDelivery
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
        db.NotificationDeliveries.Add(delivery);
        db.NotificationLogs.Add(new NotificationLog
        {
            Id = Guid.NewGuid(),
            NotificationId = parent.Id,
            DeliveryId = delivery.Id,
            Action = status == DeliveryStatus.Sent ? LogAction.Sent : status == DeliveryStatus.Skipped ? LogAction.Suppressed : LogAction.Created,
            Level = Notification.Domain.Enums.LogLevel.Info,
            Message = "Order buyer cancellation delivery recorded.",
            Metadata = JsonSerializer.Serialize(new { delivery.ErrorCode }),
            CreatedAt = now,
            UpdatedAt = now
        });
    }
    private static DomainException Conflict() => new("ORDER_BUYER_CANCELLATION_CONFLICT", "Order buyer cancellation event conflicts with an existing request or dispatch.", 409);
    private async Task<OrderBuyerCancellationBranchResponse> BranchResponse(Guid id, CancellationToken ct)
    {
        var n = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(n => n.Id == id, ct);
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().Where(d => d.NotificationId == id).OrderBy(d => d.ChannelCode).ThenBy(d => d.Id).ToListAsync(ct);
        if (n is null || deliveries.Count == 0) throw new DomainException("DELIVERY_STATE_INVALID", "Order buyer cancellation delivery state is invalid.", 409);
        return new(n.Id, n.UserId, n.Status.ToString(), n.IsInboxVisible && !n.IsDeleted, deliveries.Select(d => new OrderBuyerCancellationDeliveryResponse(d.Id, d.ChannelCode.ToString(), d.Status.ToString(), d.ErrorCode)).ToList());
    }
    private async Task<OrderBuyerCancellationResponse> Response(OrderBuyerCancellationDispatch receipt, bool replay, CancellationToken ct) =>
        new(receipt.Id, receipt.OrderId, replay, await BranchResponse(receipt.BuyerNotificationId, ct), await BranchResponse(receipt.SellerNotificationId, ct));
    public async Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var n = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId && !n.IsDeleted &&
            db.OrderBuyerCancellationDispatches.IgnoreQueryFilters().Any(r => r.BuyerNotificationId == n.Id || r.SellerNotificationId == n.Id), cancellationToken);
        if (n is null) throw new DomainException("NOTIFICATION_NOT_FOUND", "Order buyer cancellation notification was not found or does not belong to the user.", 404);
        return new(n.Id, n.UserId, n.Title, n.Body, n.Payload, n.IsRead, n.ReadAt, n.NotificationType.ToString(), n.Status.ToString(), n.CreatedAt);
    }
}
