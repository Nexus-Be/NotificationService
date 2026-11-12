namespace Notification.Application.Services.OrderCompletions;
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

public sealed class OrderCompletionService(INotificationDbContext db,TimeProvider time) : IOrderCompletionService
{
    private static DateTimeOffset Utc(DateTimeOffset v) => new(v.ToUniversalTime().Ticks / 10 * 10,TimeSpan.Zero);
    private Task<int> Lock(string key,CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8025))",ct);
    private static bool Id(Guid? value) => value.HasValue && value != Guid.Empty;
    public async Task<OrderCompletionResponse> CreateAsync(CreateOrderCompletionRequest r,CancellationToken cancellationToken = default)
    {
        var auction = string.Equals(r?.OrderKind,"Auction",StringComparison.OrdinalIgnoreCase); var regular = string.Equals(r?.OrderKind,"Regular",StringComparison.OrdinalIgnoreCase);
        var now = Utc(time.GetUtcNow());
        if (r is null || !Id(r.SourceEventId) || !Id(r.OrderId) || !Id(r.ShopId) || !Id(r.ShipmentId) || !Id(r.BuyerUserId) || !Id(r.SellerUserId) || r.BuyerUserId == r.SellerUserId ||
            (!auction && !regular) || (auction && !Id(r.AuctionId)) || (regular && r.AuctionId.HasValue) || string.IsNullOrWhiteSpace(r.OrderNo) || r.OrderNo.Length > 100 || r.OrderNo.Any(char.IsControl) ||
            !string.Equals(r.OrderStatus,"Completed",StringComparison.OrdinalIgnoreCase) || !string.Equals(r.DeliveryStatus,"Delivered",StringComparison.OrdinalIgnoreCase) ||
            r.PaidAt is null || r.DeliveredAt is null || r.CompletedAt is null || Utc(r.DeliveredAt.Value) < Utc(r.PaidAt.Value) || Utc(r.CompletedAt.Value) < Utc(r.DeliveredAt.Value) || Utc(r.CompletedAt.Value) > now.AddSeconds(30) || r.TraceId?.Length > 100 || r.TraceId?.Any(char.IsControl) == true)
            throw new DomainException("VAL_INVALID_INPUT","Order completion fields or confirmed states are invalid.");
        var kind = auction ? NotificationOrderKind.Auction : NotificationOrderKind.Regular;
        r = r with { OrderKind = kind.ToString(),OrderStatus = "Completed",DeliveryStatus = "Delivered",OrderNo = r.OrderNo.Trim(),PaidAt = Utc(r.PaidAt.Value),DeliveredAt = Utc(r.DeliveredAt.Value),CompletedAt = Utc(r.CompletedAt.Value) };
        var id = r.SourceEventId!.Value; var order = r.OrderId!.Value; var orderNo = r.OrderNo;
        var proof = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { order,kind,r.AuctionId,r.ShopId,r.ShipmentId,r.BuyerUserId,r.SellerUserId,orderNo,r.OrderStatus,r.DeliveryStatus,r.PaidAt,r.DeliveredAt,r.CompletedAt }))));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await Lock("order-completion-event:" + id.ToString("N"),cancellationToken);
        var existing = await db.OrderCompletionDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id,cancellationToken);
        if (existing is not null) { if (existing.RequestHash != proof) throw Conflict(); return await Response(existing,true,cancellationToken); }
        await Lock("order-completion-order:" + kind + ":" + order.ToString("N"),cancellationToken);
        if (r.AuctionId.HasValue) await Lock("order-completion-auction:" + r.AuctionId.Value.ToString("N"),cancellationToken);
        if (await db.OrderCompletionDispatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => (x.OrderKind == kind && x.OrderId == order) || (r.AuctionId.HasValue && x.AuctionId == r.AuctionId),cancellationToken))
            throw new DomainException("ORDER_COMPLETION_ALREADY_NOTIFIED","Order already has its completion notifications.",409);
        var codes = new[] { "ORDER_COMPLETED_BUYER_PUSH","ORDER_COMPLETED_SELLER_PUSH" }; var locale = "vi-VN";
        var templates = await db.NotificationTemplates.FromSqlInterpolated(
            $"SELECT * FROM \"NotificationTemplates\" WHERE \"Code\" = ANY ({codes}) AND \"Locale\" = {locale} AND \"IsActive\" = TRUE AND \"IsDeleted\" = FALSE ORDER BY \"Id\" FOR SHARE")
            .AsNoTracking().ToListAsync(cancellationToken);
        if (templates.Count != 2) throw new DomainException("TEMPLATE_NOT_FOUND","Active order completion templates were not found.",404);
        if (templates.Any(t => t.ChannelCode != ChannelType.Push || t.NotificationType != NotificationType.OrderCompleted || !t.IsMandatory))
            throw new DomainException("ORDER_COMPLETION_TEMPLATE_INVALID","Order completion templates must be mandatory order completed push templates.",409);
        var channels = await db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted && (c.Code == ChannelType.Push || c.Code == ChannelType.InApp)).ToListAsync(cancellationToken);
        if (channels.Count != 2) throw new DomainException("CHANNEL_NOT_FOUND","Push and in-app channel configurations are required.",409);
        var data = new Dictionary<string,string> { ["OrderNo"] = orderNo,["CompletedAtUtc"] = r.CompletedAt.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'",CultureInfo.InvariantCulture) };
        var buyer = await Branch(r.BuyerUserId!.Value,"Buyer",templates.Single(t => t.Code == codes[0]),r,data,channels,now,cancellationToken);
        var seller = await Branch(r.SellerUserId!.Value,"Seller",templates.Single(t => t.Code == codes[1]),r,data,channels,now,cancellationToken);
        var receipt = new OrderCompletionDispatch { Id = id,OrderKind = kind,OrderId = order,AuctionId = r.AuctionId,ShipmentId = r.ShipmentId!.Value,RequestHash = proof,BuyerNotificationId = buyer.Id,SellerNotificationId = seller.Id,CreatedAt = now,UpdatedAt = now };
        db.OrderCompletionDispatches.Add(receipt);
        db.AddAuditLog(AuditAction.OrderCompletionAccepted,nameof(OrderCompletionDispatch),id.ToString(),newValues:new { SourceEventId = id,OrderId = order,OrderKind = kind.ToString(),r.AuctionId,r.ShipmentId,r.BuyerUserId,r.SellerUserId,BuyerNotificationId = buyer.Id,SellerNotificationId = seller.Id },traceId:r.TraceId);
        try { await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" }) { throw Conflict(); }
        return await Response(receipt,false,cancellationToken);
    }
    private async Task<NotificationRecord> Branch(Guid user,string audience,NotificationTemplate template,CreateOrderCompletionRequest request,Dictionary<string,string> data,List<NotificationChannel> channels,DateTimeOffset now,CancellationToken ct)
    {
        var mandatory = await NotificationPreferencePolicy.MandatoryPairsAsync(db,ct);
        var disabled = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == user && p.NotificationType == NotificationType.OrderCompleted && p.IsDisabled && !p.IsDeleted).Select(p => p.ChannelCode).ToListAsync(ct);
        string? Policy(ChannelType channel) => channels.Single(c => c.Code == channel).IsDisabled ? "CHANNEL_DISABLED" : !mandatory.Contains((NotificationType.OrderCompleted,channel)) && disabled.Contains(channel) ? "PREFERENCE_DISABLED" : null;
        var inboxError = Policy(ChannelType.InApp); var inbox = inboxError is null; var pushPolicy = Policy(ChannelType.Push); var pushDisabled = pushPolicy is not null;
        var devices = pushDisabled ? new List<Guid>() : await db.DeviceTokens.AsNoTracking().Where(d => d.UserId == user && d.IsActive && !d.IsDeleted)
            .OrderBy(d => d.Id).Select(d => d.Id).Take(101).ToListAsync(ct);
        var pushError = pushDisabled ? pushPolicy : devices.Count == 0 ? "PUSH_NO_ACTIVE_DEVICES" : devices.Count > 100 ? "PUSH_DEVICE_LIMIT" : null;
        var payload = new { type = "OrderCompleted",audience,orderKind = request.OrderKind,orderId = request.OrderId,shopId = request.ShopId,auctionId = request.AuctionId,shipmentId = request.ShipmentId,orderNo = request.OrderNo,
            paidAt = request.PaidAt,deliveredAt = request.DeliveredAt,completedAt = request.CompletedAt,orderStatus = request.OrderStatus,deliveryStatus = request.DeliveryStatus };
        var row = new NotificationRecord
        {
            Id = Guid.NewGuid(),UserId = user,SourceEventId = request.SourceEventId,TemplateId = template.Id,NotificationType = NotificationType.OrderCompleted,ChannelScope = ChannelScope.All,
            Title = TemplateRenderer.Render(template.SubjectTemplate,data,false,500),Body = TemplateRenderer.Render(template.BodyTemplate,data,false,4000),Payload = JsonSerializer.Serialize(payload),
            IsInboxVisible = inbox,IsRead = !inbox,ReadAt = inbox ? null : now,IsDeleted = !inbox && pushError is not null,DeletedAt = !inbox && pushError is not null ? now : null,
            Status = pushError is null ? NotificationStatus.Pending : NotificationStatus.Completed,CreatedAt = now,UpdatedAt = now
        };
        db.Notifications.Add(row);
        AddDelivery(row,ChannelType.InApp,user.ToString(),inbox ? DeliveryStatus.Sent : DeliveryStatus.Skipped,inboxError,now);
        if (pushError is not null) AddDelivery(row,ChannelType.Push,user.ToString(),DeliveryStatus.Skipped,pushError,now);
        else foreach (var device in devices) AddDelivery(row,ChannelType.Push,device.ToString(),DeliveryStatus.Pending,null,now);
        return row;
    }
    private void AddDelivery(NotificationRecord parent,ChannelType channel,string destination,DeliveryStatus status,string? error,DateTimeOffset now)
    {
        var delivery = new NotificationDelivery { Id = Guid.NewGuid(),NotificationId = parent.Id,ChannelCode = channel,Destination = destination,Status = status,
            AttemptCount = status == DeliveryStatus.Sent ? 1 : 0,LastAttemptAt = status == DeliveryStatus.Sent ? now : null,ErrorCode = error,RowVersion = 1,CreatedAt = now,UpdatedAt = now };
        db.NotificationDeliveries.Add(delivery);
        db.NotificationLogs.Add(new NotificationLog { Id = Guid.NewGuid(),NotificationId = parent.Id,DeliveryId = delivery.Id,
            Action = status == DeliveryStatus.Sent ? LogAction.Sent : status == DeliveryStatus.Skipped ? LogAction.Suppressed : LogAction.Created,
            Level = Notification.Domain.Enums.LogLevel.Info,Message = "Order completion delivery recorded.",Metadata = JsonSerializer.Serialize(new { delivery.ErrorCode }),CreatedAt = now,UpdatedAt = now });
    }
    private static DomainException Conflict() => new("ORDER_COMPLETION_CONFLICT","Order completion event conflicts with an existing request or dispatch.",409);
    private async Task<OrderCompletionBranchResponse> BranchResponse(Guid id,CancellationToken ct)
    {
        var n = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(n => n.Id == id,ct);
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().Where(d => d.NotificationId == id).OrderBy(d => d.ChannelCode).ThenBy(d => d.Id).ToListAsync(ct);
        if (n is null || deliveries.Count == 0) throw new DomainException("DELIVERY_STATE_INVALID","Order completion delivery state is invalid.",409);
        return new(n.Id,n.UserId,n.Status.ToString(),n.IsInboxVisible && !n.IsDeleted,deliveries.Select(d => new OrderCompletionDeliveryResponse(d.Id,d.ChannelCode.ToString(),d.Status.ToString(),d.ErrorCode)).ToList());
    }
    private async Task<OrderCompletionResponse> Response(OrderCompletionDispatch receipt,bool replay,CancellationToken ct) =>
        new(receipt.Id,receipt.OrderId,replay,await BranchResponse(receipt.BuyerNotificationId,ct),await BranchResponse(receipt.SellerNotificationId,ct));
    public async Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default)
    {
        var n = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId && !n.IsDeleted &&
            db.OrderCompletionDispatches.IgnoreQueryFilters().Any(r => r.BuyerNotificationId == n.Id || r.SellerNotificationId == n.Id),cancellationToken);
        if (n is null) throw new DomainException("NOTIFICATION_NOT_FOUND","Order completion notification was not found or does not belong to the user.",404);
        return new(n.Id,n.UserId,n.Title,n.Body,n.Payload,n.IsRead,n.ReadAt,n.NotificationType.ToString(),n.Status.ToString(),n.CreatedAt);
    }
}
