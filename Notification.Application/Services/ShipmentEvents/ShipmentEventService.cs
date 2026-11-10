namespace Notification.Application.Services.ShipmentEvents;
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

public sealed class ShipmentEventService(INotificationDbContext db,TimeProvider time) : IShipmentEventService
{
    private static DateTimeOffset Utc(DateTimeOffset v) => new(v.ToUniversalTime().Ticks / 10 * 10,TimeSpan.Zero);
    private Task<int> Lock(string key,CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8024))",ct);
    private static bool Id(Guid? value) => value.HasValue && value != Guid.Empty;
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static string Code(ShipmentNotificationStatus status) => status switch
    { ShipmentNotificationStatus.PickedUp => "PICKED_UP",ShipmentNotificationStatus.InTransit => "IN_TRANSIT",ShipmentNotificationStatus.OutForDelivery => "OUT_FOR_DELIVERY",ShipmentNotificationStatus.DeliveryFailed => "DELIVERY_FAILED",_ => status.ToString().ToUpperInvariant() };
    public async Task<ShipmentEventResponse> CreateAsync(CreateShipmentEventRequest r,CancellationToken cancellationToken = default)
    {
        var name = Enum.GetNames<ShipmentNotificationStatus>().FirstOrDefault(x => string.Equals(x,r?.Status,StringComparison.OrdinalIgnoreCase));
        var auction = string.Equals(r?.OrderKind,"Auction",StringComparison.OrdinalIgnoreCase); var regular = string.Equals(r?.OrderKind,"Regular",StringComparison.OrdinalIgnoreCase);
        var needsReason = name is "DeliveryFailed" or "Returning" or "Returned" or "Cancelled"; var now = Utc(time.GetUtcNow());
        if (r is null || !Id(r.SourceEventId) || !Id(r.ShipmentId) || !Id(r.OrderId) || !Id(r.ShopId) || !Id(r.BuyerUserId) || !Id(r.SellerUserId) || r.BuyerUserId == r.SellerUserId ||
            r.SourceVersion is null or <= 0 || name is null || (!auction && !regular) || (auction && !Id(r.AuctionId)) || (regular && r.AuctionId.HasValue) ||
            string.IsNullOrWhiteSpace(r.OrderNo) || r.OrderNo.Length > 100 || r.OrderNo.Any(char.IsControl) || !string.Equals(r.Carrier,"GHN",StringComparison.OrdinalIgnoreCase) ||
            r.TrackingCode is null || !System.Text.RegularExpressions.Regex.IsMatch(r.TrackingCode,"^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$",System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
            (needsReason ? string.IsNullOrWhiteSpace(r.Reason) : r.Reason is not null) || r.Reason?.Length > 500 || r.Reason?.Any(char.IsControl) == true ||
            r.PaidAt is null || r.SellerAcceptedAt is null || r.ShipmentCreatedAt is null || r.OccurredAt is null ||
            Utc(r.PaidAt.Value) > Utc(r.SellerAcceptedAt.Value) || Utc(r.SellerAcceptedAt.Value) > Utc(r.ShipmentCreatedAt.Value) || Utc(r.ShipmentCreatedAt.Value) > Utc(r.OccurredAt.Value) ||
            Utc(r.OccurredAt.Value) > now.AddSeconds(30) || (name == "Created" && Utc(r.OccurredAt.Value) != Utc(r.ShipmentCreatedAt.Value)) || r.TraceId?.Length > 100 || r.TraceId?.Any(char.IsControl) == true)
            throw new DomainException("VAL_INVALID_INPUT","Shipment event fields or source timeline are invalid.");
        var status = Enum.Parse<ShipmentNotificationStatus>(name); var kind = auction ? NotificationOrderKind.Auction : NotificationOrderKind.Regular;
        r = r with { Status = name,OrderKind = kind.ToString(),OrderNo = r.OrderNo.Trim(),Carrier = "GHN",TrackingCode = r.TrackingCode.ToUpperInvariant(),Reason = r.Reason?.Trim(),PaidAt = Utc(r.PaidAt.Value),SellerAcceptedAt = Utc(r.SellerAcceptedAt.Value),ShipmentCreatedAt = Utc(r.ShipmentCreatedAt.Value),OccurredAt = Utc(r.OccurredAt.Value) };
        var id = r.SourceEventId!.Value; var shipment = r.ShipmentId!.Value; var version = r.SourceVersion!.Value; var occurred = r.OccurredAt.Value;
        var header = Hash(new { kind,r.OrderId,r.ShopId,r.AuctionId,r.BuyerUserId,r.SellerUserId,r.OrderNo,r.Carrier,r.TrackingCode,r.PaidAt,r.SellerAcceptedAt,r.ShipmentCreatedAt });
        var proof = Hash(new { shipment,version,header,status,r.Reason,occurred });
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await Lock("shipment-event:" + id.ToString("N"),cancellationToken);
        var old = await db.ShipmentEventDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id,cancellationToken);
        if (old is not null) { if (old.RequestHash != proof) throw Conflict(); return await Response(old,true,cancellationToken); }
        await Lock("shipment-stream:" + shipment.ToString("N"),cancellationToken);
        if (await db.ShipmentEventDispatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.ShipmentId == shipment && x.SourceVersion == version,cancellationToken))
            throw new DomainException("SHIPMENT_VERSION_ALREADY_NOTIFIED","Shipment version already has its event receipt.",409);
        var stream = await db.ShipmentNotificationStreams.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == shipment,cancellationToken);
        var existed = stream is not null;
        if (stream is not null && stream.HeaderHash != header) throw Conflict();
        if (stream is null) { stream = new() { Id = shipment,HeaderHash = header,CreatedAt = now,UpdatedAt = now }; db.ShipmentNotificationStreams.Add(stream); }
        string? suppression = null;
        if (existed && version <= stream.LastVersion) suppression = "SHIPMENT_EVENT_STALE";
        else
        {
            if (existed && occurred < stream.LastOccurredAt) throw Conflict();
            if (existed && status == stream.LastStatus) suppression = "SHIPMENT_STATUS_UNCHANGED";
            stream.LastVersion = version; stream.LastOccurredAt = occurred; stream.LastStatus = status; stream.UpdatedAt = now;
        }
        var receipt = new ShipmentEventDispatch { Id = id,ShipmentId = shipment,SourceVersion = version,RequestHash = proof,SuppressionReason = suppression,CreatedAt = now,UpdatedAt = now };
        if (suppression is null)
        {
            var prefix = "SHIPMENT_" + Code(status); var codes = new[] { prefix + "_BUYER_PUSH",prefix + "_SELLER_PUSH" }; var locale = "vi-VN";
            var templates = await db.NotificationTemplates.FromSqlInterpolated(
                $"SELECT * FROM \"NotificationTemplates\" WHERE \"Code\" = ANY ({codes}) AND \"Locale\" = {locale} AND \"IsActive\" = TRUE AND \"IsDeleted\" = FALSE ORDER BY \"Id\" FOR SHARE")
                .AsNoTracking().ToListAsync(cancellationToken);
            if (templates.Count != 2) throw new DomainException("TEMPLATE_NOT_FOUND","Active shipment event templates were not found.",404);
            if (templates.Any(t => t.ChannelCode != ChannelType.Push || t.NotificationType != NotificationType.OrderShipped)) throw new DomainException("SHIPMENT_EVENT_TEMPLATE_INVALID","Shipment event templates must use order shipped type and push channel.",409);
            var channels = await db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted && (c.Code == ChannelType.Push || c.Code == ChannelType.InApp)).ToListAsync(cancellationToken);
            if (channels.Count != 2) throw new DomainException("CHANNEL_NOT_FOUND","Push and in-app channel configurations are required.",409);
            var data = new Dictionary<string,string> { ["OrderNo"] = r.OrderNo,["TrackingCode"] = r.TrackingCode,["OccurredAtUtc"] = occurred.ToString("yyyy-MM-dd HH:mm:ss 'UTC'",CultureInfo.InvariantCulture) };
            if (needsReason) data["Reason"] = r.Reason!;
            var buyer = await Branch(r.BuyerUserId!.Value,"Buyer",templates.Single(t => t.Code == codes[0]),r,data,channels,now,cancellationToken);
            var seller = await Branch(r.SellerUserId!.Value,"Seller",templates.Single(t => t.Code == codes[1]),r,data,channels,now,cancellationToken);
            receipt.BuyerNotificationId = buyer.Id; receipt.SellerNotificationId = seller.Id;
        }
        db.ShipmentEventDispatches.Add(receipt);
        db.AddAuditLog(AuditAction.ShipmentEventAccepted,nameof(ShipmentEventDispatch),id.ToString(),newValues:new { SourceEventId = id,ShipmentId = shipment,SourceVersion = version,Status = name,SuppressionReason = suppression,receipt.BuyerNotificationId,receipt.SellerNotificationId },traceId:r.TraceId);
        try { await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" }) { throw Conflict(); }
        return await Response(receipt,false,cancellationToken);
    }
    private async Task<NotificationRecord> Branch(Guid user,string audience,NotificationTemplate template,CreateShipmentEventRequest request,Dictionary<string,string> data,List<NotificationChannel> channels,DateTimeOffset now,CancellationToken ct)
    {
        var mandatory = await NotificationPreferencePolicy.MandatoryPairsAsync(db,ct);
        var disabled = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == user && p.NotificationType == NotificationType.OrderShipped && p.IsDisabled && !p.IsDeleted).Select(p => p.ChannelCode).ToListAsync(ct);
        string? Policy(ChannelType channel) => channels.Single(c => c.Code == channel).IsDisabled ? "CHANNEL_DISABLED" : !mandatory.Contains((NotificationType.OrderShipped,channel)) && disabled.Contains(channel) ? "PREFERENCE_DISABLED" : null;
        var inboxError = Policy(ChannelType.InApp); var inbox = inboxError is null; var pushPolicy = Policy(ChannelType.Push); var pushDisabled = pushPolicy is not null;
        var devices = pushDisabled ? new List<Guid>() : await db.DeviceTokens.AsNoTracking().Where(d => d.UserId == user && d.IsActive && !d.IsDeleted)
            .OrderBy(d => d.Id).Select(d => d.Id).Take(101).ToListAsync(ct);
        var pushError = pushDisabled ? pushPolicy : devices.Count == 0 ? "PUSH_NO_ACTIVE_DEVICES" : devices.Count > 100 ? "PUSH_DEVICE_LIMIT" : null;
        var payload = new { type = "OrderShipped",audience,orderKind = request.OrderKind,orderId = request.OrderId,shopId = request.ShopId,auctionId = request.AuctionId,orderNo = request.OrderNo,
            shipmentId = request.ShipmentId,sourceVersion = request.SourceVersion,status = request.Status,carrier = request.Carrier,trackingCode = request.TrackingCode,shipmentCreatedAt = request.ShipmentCreatedAt,occurredAt = request.OccurredAt,reason = request.Reason };
        var row = new NotificationRecord
        {
            Id = Guid.NewGuid(),UserId = user,SourceEventId = request.SourceEventId,TemplateId = template.Id,NotificationType = NotificationType.OrderShipped,ChannelScope = ChannelScope.All,
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
            Level = Notification.Domain.Enums.LogLevel.Info,Message = "Shipment event delivery recorded.",Metadata = JsonSerializer.Serialize(new { delivery.ErrorCode }),CreatedAt = now,UpdatedAt = now });
    }
    private static DomainException Conflict() => new("SHIPMENT_EVENT_CONFLICT","Shipment event conflicts with an existing request or dispatch.",409);
    private async Task<ShipmentEventBranchResponse> BranchResponse(Guid id,CancellationToken ct)
    {
        var n = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(n => n.Id == id,ct);
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().Where(d => d.NotificationId == id).OrderBy(d => d.ChannelCode).ThenBy(d => d.Id).ToListAsync(ct);
        if (n is null || deliveries.Count == 0) throw new DomainException("DELIVERY_STATE_INVALID","Shipment event delivery state is invalid.",409);
        return new(n.Id,n.UserId,n.Status.ToString(),n.IsInboxVisible && !n.IsDeleted,deliveries.Select(d => new ShipmentEventDeliveryResponse(d.Id,d.ChannelCode.ToString(),d.Status.ToString(),d.ErrorCode)).ToList());
    }
    private async Task<ShipmentEventResponse> Response(ShipmentEventDispatch receipt,bool replay,CancellationToken ct)
    {
        if (receipt.SuppressionReason is not null) return new(receipt.Id,receipt.ShipmentId,receipt.SourceVersion,replay,receipt.SuppressionReason,null,null);
        if (receipt.BuyerNotificationId is null || receipt.SellerNotificationId is null) throw new DomainException("DELIVERY_STATE_INVALID","Shipment event delivery state is invalid.",409);
        return new(receipt.Id,receipt.ShipmentId,receipt.SourceVersion,replay,null,await BranchResponse(receipt.BuyerNotificationId.Value,ct),await BranchResponse(receipt.SellerNotificationId.Value,ct));
    }
    public async Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default)
    {
        var n = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId && !n.IsDeleted &&
            db.ShipmentEventDispatches.IgnoreQueryFilters().Any(r => r.BuyerNotificationId == n.Id || r.SellerNotificationId == n.Id),cancellationToken);
        if (n is null) throw new DomainException("NOTIFICATION_NOT_FOUND","Shipment event notification was not found or does not belong to the user.",404);
        return new(n.Id,n.UserId,n.Title,n.Body,n.Payload,n.IsRead,n.ReadAt,n.NotificationType.ToString(),n.Status.ToString(),n.CreatedAt);
    }
}
