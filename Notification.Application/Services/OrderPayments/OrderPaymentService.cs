namespace Notification.Application.Services.OrderPayments;

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

public sealed class OrderPaymentService(INotificationDbContext db, TimeProvider time) : IOrderPaymentService
{
    private static DateTimeOffset Utc(DateTimeOffset v) => new(v.ToUniversalTime().Ticks / 10 * 10, TimeSpan.Zero);
    private Task<int> Lock(string key, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8013))", ct);
    public async Task<OrderPaymentResponse> CreateAsync(CreateOrderPaymentRequest r, CancellationToken cancellationToken = default)
    {
        if (r is null || r.SourceEventId is null || r.SourceEventId == Guid.Empty || r.OrderId is null || r.OrderId == Guid.Empty ||
            r.ShopId is null || r.ShopId == Guid.Empty || r.BuyerUserId is null || r.BuyerUserId == Guid.Empty || r.SellerUserId is null || r.SellerUserId == Guid.Empty ||
            r.BuyerUserId == r.SellerUserId || string.IsNullOrWhiteSpace(r.OrderNo) || r.OrderNo.Length > 100 || r.OrderNo.Any(char.IsControl) ||
            r.TotalAmount is null or <= 0 or > 9999999999999999.99m || decimal.Round(r.TotalAmount.Value, 2) != r.TotalAmount ||
            !string.Equals(r.Currency, "VND", StringComparison.OrdinalIgnoreCase) || r.PaidAt is null || r.PaidAt > time.GetUtcNow().AddSeconds(30) ||
            r.SellerAcceptDeadline is null || Utc(r.SellerAcceptDeadline.Value) <= Utc(r.PaidAt.Value) || r.TraceId?.Length > 100 || r.TraceId?.Any(char.IsControl) == true)
            throw new DomainException("VAL_INVALID_INPUT", "Order payment notification fields are invalid.");
        var id = r.SourceEventId.Value; var order = r.OrderId.Value; var orderNo = r.OrderNo.Trim();
        var paidAt = Utc(r.PaidAt.Value); var deadline = Utc(r.SellerAcceptDeadline.Value);
        var proof = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { order, r.ShopId, r.BuyerUserId, r.SellerUserId, orderNo, TotalAmount = r.TotalAmount.Value.ToString("0.00", CultureInfo.InvariantCulture), Currency = "VND", paidAt, deadline }))));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await Lock("order-paid-event:" + id.ToString("N"), cancellationToken);
        var existing = await db.OrderPaidDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != proof) throw Conflict();
            return await Response(existing, true, cancellationToken);
        }
        await Lock("order-paid-order:" + order.ToString("N"), cancellationToken);
        if (await db.OrderPaidDispatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.OrderId == order, cancellationToken))
            throw new DomainException("ORDER_ALREADY_NOTIFIED", "Order already has its payment notifications.", 409);
        var codes = new[] { "ORDER_PAID_BUYER_PUSH", "ORDER_PAID_SELLER_PUSH" }; var locale = "vi-VN";
        var templates = await db.NotificationTemplates.FromSqlInterpolated(
            $"SELECT * FROM \"NotificationTemplates\" WHERE \"Code\" = ANY ({codes}) AND \"Locale\" = {locale} AND \"IsActive\" = TRUE AND \"IsDeleted\" = FALSE ORDER BY \"Id\" FOR SHARE")
            .AsNoTracking().ToListAsync(cancellationToken);
        if (templates.Count != 2) throw new DomainException("TEMPLATE_NOT_FOUND", "Active order payment templates were not found.", 404);
        if (templates.Any(t => t.ChannelCode != ChannelType.Push || t.NotificationType != NotificationType.OrderPaid || !t.IsMandatory))
            throw new DomainException("ORDER_PAYMENT_TEMPLATE_INVALID", "Order payment templates must be mandatory order paid push templates.", 409);
        var channels = await db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted && (c.Code == ChannelType.Push || c.Code == ChannelType.InApp)).ToListAsync(cancellationToken);
        if (channels.Count != 2) throw new DomainException("CHANNEL_NOT_FOUND", "Push and in-app channel configurations are required.", 409);
        var data = new Dictionary<string, string>
        {
            ["OrderNo"] = orderNo,
            ["TotalAmount"] = r.TotalAmount.Value.ToString("N2", CultureInfo.InvariantCulture),
            ["AcceptDeadlineUtc"] = deadline.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
        };
        var now = Utc(time.GetUtcNow());
        var buyer = await Branch(r.BuyerUserId.Value, "Buyer", templates.Single(t => t.Code == codes[0]), r, orderNo, paidAt, deadline, data, channels, now, cancellationToken);
        var sellerData = new Dictionary<string, string>(data); sellerData.Remove("TotalAmount");
        var seller = await Branch(r.SellerUserId.Value, "Seller", templates.Single(t => t.Code == codes[1]), r, orderNo, paidAt, deadline, sellerData, channels, now, cancellationToken);
        var receipt = new OrderPaidDispatch { Id = id, OrderId = order, RequestHash = proof, BuyerNotificationId = buyer.Id, SellerNotificationId = seller.Id, CreatedAt = now, UpdatedAt = now };
        db.OrderPaidDispatches.Add(receipt);
        db.AddAuditLog(AuditAction.OrderPaymentAccepted, nameof(OrderPaidDispatch), id.ToString(), newValues: new
        { SourceEventId = id, OrderId = order, r.ShopId, r.BuyerUserId, r.SellerUserId, BuyerNotificationId = buyer.Id, SellerNotificationId = seller.Id }, traceId: r.TraceId);
        try { await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" }) { throw Conflict(); }
        return await Response(receipt, false, cancellationToken);
    }

    private async Task<NotificationRecord> Branch(Guid user, string audience, NotificationTemplate template, CreateOrderPaymentRequest request, string orderNo,
        DateTimeOffset paidAt, DateTimeOffset deadline, Dictionary<string, string> data, List<NotificationChannel> channels, DateTimeOffset now, CancellationToken ct)
    {
        var inbox = !channels.Single(c => c.Code == ChannelType.InApp).IsDisabled;
        var pushDisabled = channels.Single(c => c.Code == ChannelType.Push).IsDisabled;
        var devices = pushDisabled ? new List<Guid>() : await db.DeviceTokens.AsNoTracking().Where(d => d.UserId == user && d.IsActive && !d.IsDeleted)
            .OrderBy(d => d.Id).Select(d => d.Id).Take(101).ToListAsync(ct);
        var pushError = pushDisabled ? "CHANNEL_DISABLED" : devices.Count == 0 ? "PUSH_NO_ACTIVE_DEVICES" : devices.Count > 100 ? "PUSH_DEVICE_LIMIT" : null;
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "OrderPaid",
            ["audience"] = audience,
            ["orderId"] = request.OrderId,
            ["shopId"] = request.ShopId,
            ["orderNo"] = orderNo,
            ["paidAt"] = paidAt,
            ["sellerAcceptDeadline"] = deadline,
            ["currency"] = "VND"
        };
        if (audience == "Buyer") payload["totalAmount"] = request.TotalAmount;
        var row = new NotificationRecord
        {
            Id = Guid.NewGuid(),
            UserId = user,
            SourceEventId = request.SourceEventId,
            TemplateId = template.Id,
            NotificationType = NotificationType.OrderPaid,
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
        AddDelivery(row, ChannelType.InApp, user.ToString(), inbox ? DeliveryStatus.Sent : DeliveryStatus.Skipped, inbox ? null : "CHANNEL_DISABLED", now);
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
            Message = "Order payment delivery recorded.",
            Metadata = JsonSerializer.Serialize(new { delivery.ErrorCode }),
            CreatedAt = now,
            UpdatedAt = now
        });
    }
    private static DomainException Conflict() => new("ORDER_PAYMENT_CONFLICT", "Order payment event conflicts with an existing request or dispatch.", 409);
    private async Task<OrderPaymentBranchResponse> BranchResponse(Guid id, CancellationToken ct)
    {
        var n = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(n => n.Id == id, ct);
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().Where(d => d.NotificationId == id).OrderBy(d => d.ChannelCode).ThenBy(d => d.Id).ToListAsync(ct);
        if (n is null || deliveries.Count == 0) throw new DomainException("DELIVERY_STATE_INVALID", "Order payment delivery state is invalid.", 409);
        return new(n.Id, n.UserId, n.Status.ToString(), n.IsInboxVisible && !n.IsDeleted, deliveries.Select(d => new OrderPaymentDeliveryResponse(d.Id, d.ChannelCode.ToString(), d.Status.ToString(), d.ErrorCode)).ToList());
    }
    private async Task<OrderPaymentResponse> Response(OrderPaidDispatch receipt, bool replay, CancellationToken ct) =>
        new(receipt.Id, receipt.OrderId, replay, await BranchResponse(receipt.BuyerNotificationId, ct), await BranchResponse(receipt.SellerNotificationId, ct));
    public async Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var n = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId && !n.IsDeleted &&
            db.OrderPaidDispatches.IgnoreQueryFilters().Any(r => r.BuyerNotificationId == n.Id || r.SellerNotificationId == n.Id), cancellationToken);
        if (n is null) throw new DomainException("NOTIFICATION_NOT_FOUND", "Order payment notification was not found or does not belong to the user.", 404);
        return new(n.Id, n.UserId, n.Title, n.Body, n.Payload, n.IsRead, n.ReadAt, n.NotificationType.ToString(), n.Status.ToString(), n.CreatedAt);
    }
}
