namespace Notification.Application.Services.AuctionOutbids;
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

public sealed class AuctionOutbidService(INotificationDbContext db,TimeProvider time) : IAuctionOutbidService
{
    private static DateTimeOffset Utc(DateTimeOffset value) => new(value.ToUniversalTime().Ticks / 10 * 10,TimeSpan.Zero);
    private static bool Id(Guid? value) => value.HasValue && value != Guid.Empty;
    private static bool Amount(decimal? value) => value is > 0 and <= 9999999999999999.99m && decimal.Round(value.Value,2) == value.Value;
    private Task<int> Lock(string key,CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8014))",ct);
    private static DomainException Conflict() => new("AUCTION_OUTBID_CONFLICT","Outbid event conflicts with an existing request or dispatch.",409);

    public async Task<AuctionOutbidResponse> CreateAsync(CreateAuctionOutbidRequest r,CancellationToken cancellationToken = default)
    {
        if (r is null || !Id(r.SourceEventId) || !Id(r.AuctionId) || !Id(r.TriggerBidId) || !Id(r.PreviousBidId) || !Id(r.PreviousHighestBidderUserId) ||
            !Id(r.NewHighestBidderUserId) || !Id(r.SellerUserId) || r.TriggerBidId == r.PreviousBidId || r.PreviousHighestBidderUserId == r.NewHighestBidderUserId ||
            r.PreviousHighestBidderUserId == r.SellerUserId || r.NewHighestBidderUserId == r.SellerUserId ||
            string.IsNullOrWhiteSpace(r.AuctionTitle) || r.AuctionTitle.Length > 200 || r.AuctionTitle.Any(char.IsControl) ||
            !Amount(r.PreviousAmount) || !Amount(r.NewAmount) || r.NewAmount <= r.PreviousAmount || !string.Equals(r.Currency,"VND",StringComparison.OrdinalIgnoreCase) ||
            r.OccurredAt is null || r.OccurredAt > time.GetUtcNow().AddSeconds(30) || r.AuctionEndsAt is null || Utc(r.AuctionEndsAt.Value) <= Utc(r.OccurredAt.Value) ||
            r.TraceId?.Length > 100 || r.TraceId?.Any(char.IsControl) == true)
            throw new DomainException("VAL_INVALID_INPUT","Auction outbid notification fields are invalid.");
        var eventId = r.SourceEventId!.Value; var auction = r.AuctionId!.Value; var trigger = r.TriggerBidId!.Value; var previous = r.PreviousBidId!.Value;
        var target = r.PreviousHighestBidderUserId!.Value; var title = r.AuctionTitle.Trim(); var occurred = Utc(r.OccurredAt.Value); var ends = Utc(r.AuctionEndsAt.Value);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { auction,trigger,previous,target,r.NewHighestBidderUserId,r.SellerUserId,title,PreviousAmount = r.PreviousAmount!.Value.ToString("0.00",CultureInfo.InvariantCulture),
            NewAmount = r.NewAmount!.Value.ToString("0.00",CultureInfo.InvariantCulture),Currency = "VND",occurred,ends }))));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await Lock("outbid-event:" + eventId.ToString("N"),cancellationToken);
        var old = await db.AuctionOutbidDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId,cancellationToken);
        if (old is not null) { if (old.RequestHash != hash) throw Conflict(); return await Response(old,true,cancellationToken); }
        await Lock("outbid-trigger:" + trigger.ToString("N"),cancellationToken);
        await Lock("outbid-previous:" + previous.ToString("N"),cancellationToken);
        if (await db.AuctionOutbidDispatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.TriggerBidId == trigger || x.PreviousBidId == previous,cancellationToken))
            throw new DomainException("AUCTION_BID_ALREADY_NOTIFIED","Bid transition already has an outbid notification.",409);
        var code = "AUCTION_BID_OUTBID_PUSH"; var locale = "vi-VN";
        var template = await db.NotificationTemplates.FromSqlInterpolated(
            $"SELECT * FROM \"NotificationTemplates\" WHERE \"Code\" = {code} AND \"Locale\" = {locale} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(t => t.IsActive,cancellationToken);
        if (template is null) throw new DomainException("TEMPLATE_NOT_FOUND","Active auction outbid template was not found.",404);
        if (template.ChannelCode != ChannelType.Push || template.NotificationType != NotificationType.AuctionOutbid)
            throw new DomainException("AUCTION_OUTBID_TEMPLATE_INVALID","Outbid template must use the auction outbid type and push channel.",409);
        var channels = await db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted && (c.Code == ChannelType.InApp || c.Code == ChannelType.Push)).ToListAsync(cancellationToken);
        if (channels.Count != 2) throw new DomainException("CHANNEL_NOT_FOUND","Push and in-app channel configurations are required.",409);
        var mandatory = await NotificationPreferencePolicy.MandatoryPairsAsync(db,cancellationToken);
        var preferences = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == target && p.NotificationType == NotificationType.AuctionOutbid && !p.IsDeleted && p.IsDisabled)
            .Select(p => p.ChannelCode).ToListAsync(cancellationToken);
        string? Policy(ChannelType channel) => channels.Single(c => c.Code == channel).IsDisabled ? "CHANNEL_DISABLED" :
            !mandatory.Contains((NotificationType.AuctionOutbid,channel)) && preferences.Contains(channel) ? "PREFERENCE_DISABLED" : null;
        var inAppError = Policy(ChannelType.InApp); var pushError = Policy(ChannelType.Push);
        var devices = pushError is not null ? new List<Guid>() : await db.DeviceTokens.AsNoTracking().Where(d => d.UserId == target && d.IsActive && !d.IsDeleted)
            .OrderBy(d => d.Id).Select(d => d.Id).Take(101).ToListAsync(cancellationToken);
        pushError ??= devices.Count == 0 ? "PUSH_NO_ACTIVE_DEVICES" : devices.Count > 100 ? "PUSH_DEVICE_LIMIT" : null;
        var now = Utc(time.GetUtcNow()); var visible = inAppError is null; var hidden = !visible && pushError is not null;
        var data = new Dictionary<string,string> { ["AuctionTitle"] = title,["NewAmount"] = r.NewAmount.Value.ToString("N2",CultureInfo.InvariantCulture),
            ["OccurredAtUtc"] = occurred.ToString("yyyy-MM-dd HH:mm:ss 'UTC'",CultureInfo.InvariantCulture) };
        var row = new NotificationRecord
        {
            Id = Guid.NewGuid(),UserId = target,SourceEventId = eventId,TemplateId = template.Id,NotificationType = NotificationType.AuctionOutbid,ChannelScope = ChannelScope.All,
            Title = TemplateRenderer.Render(template.SubjectTemplate,data,false,500),Body = TemplateRenderer.Render(template.BodyTemplate,data,false,4000),
            Payload = JsonSerializer.Serialize(new { type = "AuctionOutbid",auctionId = auction,triggerBidId = trigger,previousBidId = previous,
                newAmount = r.NewAmount.Value,currency = "VND",occurredAt = occurred,auctionEndsAt = ends }),
            IsInboxVisible = visible,IsRead = !visible,ReadAt = visible ? null : now,IsDeleted = hidden,DeletedAt = hidden ? now : null,
            Status = pushError is null ? NotificationStatus.Pending : NotificationStatus.Completed,CreatedAt = now,UpdatedAt = now
        };
        db.Notifications.Add(row);
        AddDelivery(row,ChannelType.InApp,target.ToString(),visible ? DeliveryStatus.Sent : DeliveryStatus.Skipped,inAppError,now);
        if (pushError is not null) AddDelivery(row,ChannelType.Push,target.ToString(),DeliveryStatus.Skipped,pushError,now);
        else foreach (var device in devices) AddDelivery(row,ChannelType.Push,device.ToString(),DeliveryStatus.Pending,null,now);
        var receipt = new AuctionOutbidDispatch { Id = eventId,AuctionId = auction,TriggerBidId = trigger,PreviousBidId = previous,NotificationId = row.Id,RequestHash = hash,CreatedAt = now,UpdatedAt = now };
        db.AuctionOutbidDispatches.Add(receipt);
        db.AddAuditLog(AuditAction.AuctionOutbidAccepted,nameof(AuctionOutbidDispatch),eventId.ToString(),newValues:new
        { SourceEventId = eventId,AuctionId = auction,TriggerBidId = trigger,PreviousBidId = previous,TargetUserId = target,NotificationId = row.Id },traceId:r.TraceId);
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
            Level = Notification.Domain.Enums.LogLevel.Info,Message = "Auction outbid delivery recorded.",Metadata = JsonSerializer.Serialize(new { d.ErrorCode }),CreatedAt = now,UpdatedAt = now });
    }
    private async Task<AuctionOutbidResponse> Response(AuctionOutbidDispatch r,bool replay,CancellationToken ct)
    {
        var row = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.NotificationId,ct);
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().Where(x => x.NotificationId == r.NotificationId).OrderBy(x => x.ChannelCode).ThenBy(x => x.Id).ToListAsync(ct);
        if (row is null || deliveries.Count == 0) throw new DomainException("DELIVERY_STATE_INVALID","Auction outbid delivery state is invalid.",409);
        return new(r.Id,r.AuctionId,row.Id,row.UserId,row.Status.ToString(),row.IsInboxVisible && !row.IsDeleted,replay,
            deliveries.Select(d => new AuctionOutbidDeliveryResponse(d.Id,d.ChannelCode.ToString(),d.Status.ToString(),d.ErrorCode)).ToList());
    }
    public async Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default)
    {
        var row = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId && !n.IsDeleted &&
            db.AuctionOutbidDispatches.IgnoreQueryFilters().Any(r => r.NotificationId == n.Id),cancellationToken);
        if (row is null) throw new DomainException("NOTIFICATION_NOT_FOUND","Outbid notification was not found or does not belong to the user.",404);
        return new(row.Id,row.UserId,row.Title,row.Body,row.Payload,row.IsRead,row.ReadAt,row.NotificationType.ToString(),row.Status.ToString(),row.CreatedAt);
    }
}
