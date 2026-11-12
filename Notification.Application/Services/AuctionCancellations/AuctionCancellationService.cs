namespace Notification.Application.Services.AuctionCancellations;
using System.Data.Common;
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

public sealed class AuctionCancellationService(INotificationDbContext db,TimeProvider time) : IAuctionCancellationService
{
    private static DateTimeOffset Utc(DateTimeOffset v) => new(v.ToUniversalTime().Ticks / 10 * 10,TimeSpan.Zero);
    private static bool Id(Guid? v) => v.HasValue && v != Guid.Empty;
    private static DomainException Invalid() => new("VAL_INVALID_INPUT","Auction cancellation notification fields are invalid.");
    private static DomainException Conflict() => new("AUCTION_CANCELLATION_CONFLICT","Cancellation event conflicts with an existing header or dispatch.",409);
    private Task<int> Lock(string key,CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8016))",ct);

    public async Task<AuctionCancellationResponse> CreateAsync(CreateAuctionCancellationRequest r,CancellationToken cancellationToken = default)
    {
        if (r is null || !Id(r.SourceEventId) || !Id(r.AuctionId) || !Id(r.SellerUserId) || !Id(r.RecipientUserId) || r.BidCount is null or < 0 ||
            string.IsNullOrWhiteSpace(r.AuctionTitle) || r.AuctionTitle.Length > 200 || r.AuctionTitle.Any(char.IsControl) || string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 500 || r.Reason.Any(char.IsControl) ||
            r.TraceId?.Length > 100 || r.TraceId?.Any(char.IsControl) == true || r.CancelledAt is null || r.CancelledAt > time.GetUtcNow().AddSeconds(30)) throw Invalid();
        var kind = r.CancellationKind?.ToLowerInvariant(); var audience = r.RecipientKind?.ToLowerInvariant();
        if (kind is not ("seller" or "admin") || audience is not ("seller" or "participant") ||
            (audience == "seller" && r.RecipientUserId != r.SellerUserId) || (audience == "participant" && (r.RecipientUserId == r.SellerUserId || r.BidCount == 0)) ||
            (kind == "seller" && (r.BidCount != 0 || audience != "seller"))) throw Invalid();
        var id = r.SourceEventId!.Value; var auction = r.AuctionId!.Value; var user = r.RecipientUserId!.Value;
        var title = r.AuctionTitle.Trim(); var reason = r.Reason.Trim(); var cancelled = Utc(r.CancelledAt.Value);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { auction,r.SellerUserId,title,reason,kind,r.BidCount,cancelled }))));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await Lock("auction-cancel-event:" + id.ToString("N"),cancellationToken);
        var header = await db.AuctionCancellationDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id,cancellationToken);
        if (header is not null && header.RequestHash != hash) throw Conflict();
        if (header is null)
        {
            await Lock("auction-cancel-auction:" + auction.ToString("N"),cancellationToken);
            if (await db.AuctionCancellationDispatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.AuctionId == auction,cancellationToken))
                throw new DomainException("AUCTION_CANCELLATION_ALREADY_NOTIFIED","Auction already has a cancellation notification event.",409);
            var nowHeader = Utc(time.GetUtcNow());
            header = new() { Id = id,AuctionId = auction,RequestHash = hash,CreatedAt = nowHeader,UpdatedAt = nowHeader };
            db.AuctionCancellationDispatches.Add(header);
        }
        var old = await db.AuctionCancellationRecipients.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.SourceEventId == id && x.UserId == user,cancellationToken);
        if (old is not null) return await Response(header,old,true,cancellationToken);
        var code = "AUCTION_CANCELLED_PUSH"; var locale = "vi-VN";
        var template = await db.NotificationTemplates.FromSqlInterpolated($"SELECT * FROM \"NotificationTemplates\" WHERE \"Code\" = {code} AND \"Locale\" = {locale} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(t => t.IsActive,cancellationToken);
        if (template is null) throw new DomainException("TEMPLATE_NOT_FOUND","Active cancellation template was not found.",404);
        if (template.ChannelCode != ChannelType.Push || template.NotificationType != NotificationType.AuctionCancelled || !template.IsMandatory)
            throw new DomainException("AUCTION_CANCELLATION_TEMPLATE_INVALID","Cancellation template must be mandatory auction cancelled push.",409);
        var channels = await db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted && (c.Code == ChannelType.Push || c.Code == ChannelType.InApp)).ToListAsync(cancellationToken);
        if (channels.Count != 2) throw new DomainException("CHANNEL_NOT_FOUND","Push and in-app channel configurations are required.",409);
        var mandatory = await NotificationPreferencePolicy.MandatoryPairsAsync(db,cancellationToken);
        var preferences = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == user && p.NotificationType == NotificationType.AuctionCancelled && p.IsDisabled && !p.IsDeleted).Select(p => p.ChannelCode).ToListAsync(cancellationToken);
        string? Policy(ChannelType c) => channels.Single(x => x.Code == c).IsDisabled ? "CHANNEL_DISABLED" : !mandatory.Contains((NotificationType.AuctionCancelled,c)) && preferences.Contains(c) ? "PREFERENCE_DISABLED" : null;
        var inAppError = Policy(ChannelType.InApp); var pushError = Policy(ChannelType.Push);
        var devices = pushError is not null ? new List<Guid>() : await db.DeviceTokens.AsNoTracking().Where(d => d.UserId == user && d.IsActive && !d.IsDeleted).OrderBy(d => d.Id).Select(d => d.Id).Take(101).ToListAsync(cancellationToken);
        pushError ??= devices.Count == 0 ? "PUSH_NO_ACTIVE_DEVICES" : devices.Count > 100 ? "PUSH_DEVICE_LIMIT" : null;
        var data = new Dictionary<string,string> { ["AuctionTitle"] = title,["Reason"] = reason,["CancelledAtUtc"] = cancelled.ToString("yyyy-MM-dd HH:mm:ss 'UTC'",System.Globalization.CultureInfo.InvariantCulture) };
        var now = Utc(time.GetUtcNow()); var visible = inAppError is null; var hidden = !visible && pushError is not null;
        var row = new NotificationRecord
        {
            Id = Guid.NewGuid(),UserId = user,SourceEventId = id,TemplateId = template.Id,NotificationType = NotificationType.AuctionCancelled,ChannelScope = ChannelScope.All,
            Title = TemplateRenderer.Render(template.SubjectTemplate,data,false,500),Body = TemplateRenderer.Render(template.BodyTemplate,data,false,4000),
            Payload = JsonSerializer.Serialize(new { type = "AuctionCancelled",auctionId = auction,cancelledAt = cancelled,cancellationKind = kind == "seller" ? "Seller" : "Admin",recipientKind = audience == "seller" ? "Seller" : "Participant" }),
            IsInboxVisible = visible,IsRead = !visible,ReadAt = visible ? null : now,IsDeleted = hidden,DeletedAt = hidden ? now : null,
            Status = pushError is null ? NotificationStatus.Pending : NotificationStatus.Completed,CreatedAt = now,UpdatedAt = now
        };
        db.Notifications.Add(row); AddDelivery(row,ChannelType.InApp,user.ToString(),visible ? DeliveryStatus.Sent : DeliveryStatus.Skipped,inAppError,now);
        if (pushError is not null) AddDelivery(row,ChannelType.Push,user.ToString(),DeliveryStatus.Skipped,pushError,now);
        else foreach (var device in devices) AddDelivery(row,ChannelType.Push,device.ToString(),DeliveryStatus.Pending,null,now);
        var recipient = new AuctionCancellationRecipient { Id = Guid.NewGuid(),SourceEventId = id,UserId = user,NotificationId = row.Id,CreatedAt = now,UpdatedAt = now };
        db.AuctionCancellationRecipients.Add(recipient);
        db.AddAuditLog(AuditAction.AuctionCancellationAccepted,nameof(AuctionCancellationRecipient),recipient.Id.ToString(),newValues:new
        { SourceEventId = id,AuctionId = auction,RecipientUserId = user,NotificationId = row.Id },traceId:r.TraceId);
        try { await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" }) { throw Conflict(); }
        return await Response(header,recipient,false,cancellationToken);
    }
    private void AddDelivery(NotificationRecord parent,ChannelType channel,string destination,DeliveryStatus status,string? error,DateTimeOffset now)
    {
        var d = new NotificationDelivery { Id = Guid.NewGuid(),NotificationId = parent.Id,ChannelCode = channel,Destination = destination,Status = status,
            AttemptCount = status == DeliveryStatus.Sent ? 1 : 0,LastAttemptAt = status == DeliveryStatus.Sent ? now : null,ErrorCode = error,RowVersion = 1,CreatedAt = now,UpdatedAt = now };
        db.NotificationDeliveries.Add(d);
        db.NotificationLogs.Add(new NotificationLog { Id = Guid.NewGuid(),NotificationId = parent.Id,DeliveryId = d.Id,
            Action = status == DeliveryStatus.Sent ? LogAction.Sent : status == DeliveryStatus.Skipped ? LogAction.Suppressed : LogAction.Created,
            Level = Notification.Domain.Enums.LogLevel.Info,Message = "Auction cancellation recipient delivery recorded.",Metadata = JsonSerializer.Serialize(new { d.ErrorCode }),CreatedAt = now,UpdatedAt = now });
    }
    private async Task<AuctionCancellationResponse> Response(AuctionCancellationDispatch header,AuctionCancellationRecipient recipient,bool replay,CancellationToken ct)
    {
        var n = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == recipient.NotificationId,ct);
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().Where(x => x.NotificationId == recipient.NotificationId).OrderBy(x => x.ChannelCode).ThenBy(x => x.Id).ToListAsync(ct);
        if (n is null || deliveries.Count == 0) throw new DomainException("DELIVERY_STATE_INVALID","Cancellation recipient delivery state is invalid.",409);
        return new(header.Id,header.AuctionId,n.Id,n.UserId,n.Status.ToString(),n.IsInboxVisible && !n.IsDeleted,replay,deliveries.Select(d => new AuctionCancellationDeliveryResponse(d.Id,d.ChannelCode.ToString(),d.Status.ToString(),d.ErrorCode)).ToList());
    }
    public async Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default)
    {
        var n = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId && !n.IsDeleted &&
            db.AuctionCancellationRecipients.IgnoreQueryFilters().Any(r => r.NotificationId == n.Id),cancellationToken);
        if (n is null) throw new DomainException("NOTIFICATION_NOT_FOUND","Cancellation notification was not found or does not belong to the user.",404);
        return new(n.Id,n.UserId,n.Title,n.Body,n.Payload,n.IsRead,n.ReadAt,n.NotificationType.ToString(),n.Status.ToString(),n.CreatedAt);
    }
}
