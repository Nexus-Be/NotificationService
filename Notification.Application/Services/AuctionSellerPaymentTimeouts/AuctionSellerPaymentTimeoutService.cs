namespace Notification.Application.Services.AuctionSellerPaymentTimeouts;
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

public sealed class AuctionSellerPaymentTimeoutService(INotificationDbContext db,TimeProvider time) : IAuctionSellerPaymentTimeoutService
{
    private static DateTimeOffset Utc(DateTimeOffset value) => new(value.ToUniversalTime().Ticks / 10 * 10,TimeSpan.Zero);
    private static bool Id(Guid? value) => value.HasValue && value != Guid.Empty;
    private Task<int> Lock(string key,CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8019))",ct);
    private static DomainException Conflict() => new("AUCTION_SELLER_PAYMENT_TIMEOUT_CONFLICT","Seller timeout event conflicts with an existing request or dispatch.",409);

    public async Task<AuctionSellerPaymentTimeoutResponse> CreateAsync(CreateAuctionSellerPaymentTimeoutRequest r,CancellationToken cancellationToken = default)
    {
        var now = Utc(time.GetUtcNow());
        if (r is null || !Id(r.SourceEventId) || !Id(r.AuctionId) || !Id(r.ResultId) || !Id(r.WinningBidId) || !Id(r.OrderId) || !Id(r.WinnerUserId) || !Id(r.SellerUserId) ||
            r.WinnerUserId == r.SellerUserId || string.IsNullOrWhiteSpace(r.AuctionTitle) || r.AuctionTitle.Length > 200 || r.AuctionTitle.Any(char.IsControl) ||
            !string.Equals(r.OrderStatus,"Cancelled",StringComparison.OrdinalIgnoreCase) || !string.Equals(r.AuctionStatus,"Failed",StringComparison.OrdinalIgnoreCase) ||
            r.SettledAt is null || r.SettledAt > now.AddSeconds(30) || r.PaymentDeadline is null || Utc(r.PaymentDeadline.Value) <= Utc(r.SettledAt.Value) ||
            Utc(r.PaymentDeadline.Value) > now || r.TimedOutAt is null || Utc(r.TimedOutAt.Value) < Utc(r.PaymentDeadline.Value) || r.TimedOutAt > now.AddSeconds(30) ||
            r.TraceId?.Length > 100 || r.TraceId?.Any(char.IsControl) == true)
            throw new DomainException("VAL_INVALID_INPUT","Seller payment timeout fields or confirmed states are invalid.");
        var eventId = r.SourceEventId!.Value; var auction = r.AuctionId!.Value; var result = r.ResultId!.Value; var target = r.SellerUserId!.Value;
        var title = r.AuctionTitle.Trim(); var settled = Utc(r.SettledAt.Value); var deadline = Utc(r.PaymentDeadline.Value); var timedOut = Utc(r.TimedOutAt.Value);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { auction,result,target,r.WinningBidId,r.WinnerUserId,r.OrderId,title,OrderStatus = "Cancelled",AuctionStatus = "Failed",settled,deadline,timedOut }))));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await Lock("seller-timeout-event:" + eventId.ToString("N"),cancellationToken);
        var old = await db.AuctionSellerPaymentTimeoutDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId,cancellationToken);
        if (old is not null) { if (old.RequestHash != hash) throw Conflict(); return await Response(old,true,cancellationToken); }
        await Lock("seller-timeout-auction:" + auction.ToString("N"),cancellationToken);
        await Lock("seller-timeout-order:" + r.OrderId!.Value.ToString("N"),cancellationToken);
        await Lock("seller-timeout-result:" + result.ToString("N"),cancellationToken);
        await Lock("seller-timeout-bid:" + r.WinningBidId!.Value.ToString("N"),cancellationToken);
        if (await db.AuctionSellerPaymentTimeoutDispatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.AuctionId == auction || x.ResultId == result || x.WinningBidId == r.WinningBidId || x.OrderId == r.OrderId,cancellationToken))
            throw new DomainException("AUCTION_SELLER_PAYMENT_TIMEOUT_ALREADY_NOTIFIED","Auction payment outcome already has a seller timeout notification.",409);
        var code = "AUCTION_SELLER_PAYMENT_TIMEOUT_PUSH"; var locale = "vi-VN";
        var template = await db.NotificationTemplates.FromSqlInterpolated(
            $"SELECT * FROM \"NotificationTemplates\" WHERE \"Code\" = {code} AND \"Locale\" = {locale} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(t => t.IsActive,cancellationToken);
        if (template is null) throw new DomainException("TEMPLATE_NOT_FOUND","Active seller payment timeout template was not found.",404);
        if (template.ChannelCode != ChannelType.Push || template.NotificationType != NotificationType.AuctionPaymentTimedOut)
            throw new DomainException("AUCTION_SELLER_PAYMENT_TIMEOUT_TEMPLATE_INVALID","Seller timeout template must use the auction payment timed out type and push channel.",409);
        var channels = await db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted && (c.Code == ChannelType.InApp || c.Code == ChannelType.Push)).ToListAsync(cancellationToken);
        if (channels.Count != 2) throw new DomainException("CHANNEL_NOT_FOUND","Push and in-app channel configurations are required.",409);
        var mandatory = await NotificationPreferencePolicy.MandatoryPairsAsync(db,cancellationToken);
        var preferences = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == target && p.NotificationType == NotificationType.AuctionPaymentTimedOut && !p.IsDeleted && p.IsDisabled)
            .Select(p => p.ChannelCode).ToListAsync(cancellationToken);
        string? Policy(ChannelType channel) => channels.Single(c => c.Code == channel).IsDisabled ? "CHANNEL_DISABLED" :
            !mandatory.Contains((NotificationType.AuctionPaymentTimedOut,channel)) && preferences.Contains(channel) ? "PREFERENCE_DISABLED" : null;
        var inAppError = Policy(ChannelType.InApp); var pushError = Policy(ChannelType.Push);
        var devices = pushError is not null ? new List<Guid>() : await db.DeviceTokens.AsNoTracking().Where(d => d.UserId == target && d.IsActive && !d.IsDeleted)
            .OrderBy(d => d.Id).Select(d => d.Id).Take(101).ToListAsync(cancellationToken);
        pushError ??= devices.Count == 0 ? "PUSH_NO_ACTIVE_DEVICES" : devices.Count > 100 ? "PUSH_DEVICE_LIMIT" : null;
        var visible = inAppError is null; var hidden = !visible && pushError is not null;
        var data = new Dictionary<string,string> { ["AuctionTitle"] = title,["PaymentDeadlineUtc"] = deadline.ToString("yyyy-MM-dd HH:mm:ss 'UTC'",CultureInfo.InvariantCulture),["TimedOutAtUtc"] = timedOut.ToString("yyyy-MM-dd HH:mm:ss 'UTC'",CultureInfo.InvariantCulture) };
        var row = new NotificationRecord
        {
            Id = Guid.NewGuid(),UserId = target,SourceEventId = eventId,TemplateId = template.Id,NotificationType = NotificationType.AuctionPaymentTimedOut,ChannelScope = ChannelScope.All,
            Title = TemplateRenderer.Render(template.SubjectTemplate,data,false,500),Body = TemplateRenderer.Render(template.BodyTemplate,data,false,4000),
            Payload = JsonSerializer.Serialize(new { type = "AuctionPaymentTimedOut",audience = "Seller",auctionId = auction,resultId = result,winningBidId = r.WinningBidId,orderId = r.OrderId,
                settledAt = settled,paymentDeadline = deadline,timedOutAt = timedOut,orderStatus = "Cancelled",auctionStatus = "Failed" }),
            IsInboxVisible = visible,IsRead = !visible,ReadAt = visible ? null : now,IsDeleted = hidden,DeletedAt = hidden ? now : null,
            Status = pushError is null ? NotificationStatus.Pending : NotificationStatus.Completed,CreatedAt = now,UpdatedAt = now
        };
        db.Notifications.Add(row);
        AddDelivery(row,ChannelType.InApp,target.ToString(),visible ? DeliveryStatus.Sent : DeliveryStatus.Skipped,inAppError,now);
        if (pushError is not null) AddDelivery(row,ChannelType.Push,target.ToString(),DeliveryStatus.Skipped,pushError,now);
        else foreach (var device in devices) AddDelivery(row,ChannelType.Push,device.ToString(),DeliveryStatus.Pending,null,now);
        var receipt = new AuctionSellerPaymentTimeoutDispatch { Id = eventId,AuctionId = auction,ResultId = result,WinningBidId = r.WinningBidId!.Value,OrderId = r.OrderId!.Value,NotificationId = row.Id,RequestHash = hash,CreatedAt = now,UpdatedAt = now };
        db.AuctionSellerPaymentTimeoutDispatches.Add(receipt);
        db.AddAuditLog(AuditAction.AuctionSellerPaymentTimeoutAccepted,nameof(AuctionSellerPaymentTimeoutDispatch),eventId.ToString(),newValues:new
        { SourceEventId = eventId,AuctionId = auction,ResultId = result,SellerUserId = target,OrderId = r.OrderId,NotificationId = row.Id },traceId:r.TraceId);
        try { await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" }) { throw Conflict(); }
        return await Response(receipt,false,cancellationToken);
    }
    private void AddDelivery(NotificationRecord parent,ChannelType channel,string destination,DeliveryStatus status,string? error,DateTimeOffset now)
    {
        var d = new NotificationDelivery { Id = Guid.NewGuid(),NotificationId = parent.Id,ChannelCode = channel,Destination = destination,Status = status,
            AttemptCount = status == DeliveryStatus.Sent ? 1 : 0,LastAttemptAt = status == DeliveryStatus.Sent ? now : null,ErrorCode = error,RowVersion = 1,CreatedAt = now,UpdatedAt = now };
        db.NotificationDeliveries.Add(d);
        db.NotificationLogs.Add(new NotificationLog { Id = Guid.NewGuid(),NotificationId = parent.Id,DeliveryId = d.Id,
            Action = status == DeliveryStatus.Sent ? LogAction.Sent : status == DeliveryStatus.Skipped ? LogAction.Suppressed : LogAction.Created,
            Level = Notification.Domain.Enums.LogLevel.Info,Message = "Seller payment timeout delivery recorded.",Metadata = JsonSerializer.Serialize(new { d.ErrorCode }),CreatedAt = now,UpdatedAt = now });
    }
    private async Task<AuctionSellerPaymentTimeoutResponse> Response(AuctionSellerPaymentTimeoutDispatch r,bool replay,CancellationToken ct)
    {
        var row = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.NotificationId,ct);
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().Where(x => x.NotificationId == r.NotificationId).OrderBy(x => x.ChannelCode).ThenBy(x => x.Id).ToListAsync(ct);
        if (row is null || deliveries.Count == 0) throw new DomainException("DELIVERY_STATE_INVALID","Seller payment timeout delivery state is invalid.",409);
        return new(r.Id,r.AuctionId,r.OrderId,row.Id,row.UserId,row.Status.ToString(),row.IsInboxVisible && !row.IsDeleted,replay,
            deliveries.Select(d => new AuctionSellerPaymentTimeoutDeliveryResponse(d.Id,d.ChannelCode.ToString(),d.Status.ToString(),d.ErrorCode)).ToList());
    }
    public async Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default)
    {
        var row = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId && !n.IsDeleted &&
            db.AuctionSellerPaymentTimeoutDispatches.IgnoreQueryFilters().Any(r => r.NotificationId == n.Id),cancellationToken);
        if (row is null) throw new DomainException("NOTIFICATION_NOT_FOUND","Seller timeout notification was not found or does not belong to the user.",404);
        return new(row.Id,row.UserId,row.Title,row.Body,row.Payload,row.IsRead,row.ReadAt,row.NotificationType.ToString(),row.Status.ToString(),row.CreatedAt);
    }
}
