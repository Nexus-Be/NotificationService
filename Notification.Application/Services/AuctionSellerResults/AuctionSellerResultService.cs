namespace Notification.Application.Services.AuctionSellerResults;
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

public sealed class AuctionSellerResultService(INotificationDbContext db,TimeProvider time) : IAuctionSellerResultService
{
    private static DateTimeOffset Utc(DateTimeOffset value) => new(value.ToUniversalTime().Ticks / 10 * 10,TimeSpan.Zero);
    private static bool Id(Guid? value) => value.HasValue && value != Guid.Empty;
    private static bool Amount(decimal? value) => value is > 0 and <= 9999999999999999.99m && decimal.Round(value.Value,2) == value.Value;
    private Task<int> Lock(string key,CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8018))",ct);
    private static DomainException Conflict() => new("AUCTION_SELLER_RESULT_CONFLICT","Seller result event conflicts with an existing request or dispatch.",409);

    public async Task<AuctionSellerResultResponse> CreateAsync(CreateAuctionSellerResultRequest r,CancellationToken cancellationToken = default)
    {
        var winnerSelected = string.Equals(r?.Outcome,"WinnerSelected",StringComparison.OrdinalIgnoreCase);
        var noBids = string.Equals(r?.Outcome,"NoBids",StringComparison.OrdinalIgnoreCase);
        if (r is null || !Id(r.SourceEventId) || !Id(r.AuctionId) || !Id(r.ResultId) || !Id(r.SellerUserId) || (!winnerSelected && !noBids) ||
            string.IsNullOrWhiteSpace(r.AuctionTitle) || r.AuctionTitle.Length > 200 || r.AuctionTitle.Any(char.IsControl) ||
            r.DecidedAt is null || r.DecidedAt > time.GetUtcNow().AddSeconds(30) || r.TraceId?.Length > 100 || r.TraceId?.Any(char.IsControl) == true ||
            (winnerSelected && (!Id(r.WinningBidId) || !Id(r.WinnerUserId) || !Id(r.OrderId) || r.WinnerUserId == r.SellerUserId || !Amount(r.WinningAmount) ||
                !string.Equals(r.Currency,"VND",StringComparison.OrdinalIgnoreCase) || r.PaymentDeadline is null || Utc(r.PaymentDeadline.Value) <= Utc(r.DecidedAt.Value))) ||
            (noBids && (r.WinningBidId.HasValue || r.WinnerUserId.HasValue || r.OrderId.HasValue || r.WinningAmount.HasValue || r.Currency is not null || r.PaymentDeadline.HasValue)))
            throw new DomainException("VAL_INVALID_INPUT","Seller auction result fields or outcome are invalid.");
        var eventId = r.SourceEventId!.Value; var auction = r.AuctionId!.Value; var result = r.ResultId!.Value; var target = r.SellerUserId!.Value;
        var outcome = winnerSelected ? "WinnerSelected" : "NoBids"; var title = r.AuctionTitle.Trim(); var decided = Utc(r.DecidedAt.Value);
        DateTimeOffset? deadline = r.PaymentDeadline is null ? null : Utc(r.PaymentDeadline.Value);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { auction,result,target,outcome,title,r.WinningBidId,r.WinnerUserId,r.OrderId,Amount = r.WinningAmount?.ToString("0.00",CultureInfo.InvariantCulture),Currency = winnerSelected ? "VND" : null,decided,deadline }))));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await Lock("seller-result-event:" + eventId.ToString("N"),cancellationToken);
        var old = await db.AuctionSellerResultDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId,cancellationToken);
        if (old is not null) { if (old.RequestHash != hash) throw Conflict(); return await Response(old,true,cancellationToken); }
        await Lock("seller-result-auction:" + auction.ToString("N"),cancellationToken);
        await Lock("seller-result-result:" + result.ToString("N"),cancellationToken);
        if (r.WinningBidId.HasValue) await Lock("seller-result-bid:" + r.WinningBidId.Value.ToString("N"),cancellationToken);
        if (r.OrderId.HasValue) await Lock("seller-result-order:" + r.OrderId.Value.ToString("N"),cancellationToken);
        if (await db.AuctionSellerResultDispatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.AuctionId == auction || x.ResultId == result ||
            (r.WinningBidId.HasValue && x.WinningBidId == r.WinningBidId) || (r.OrderId.HasValue && x.OrderId == r.OrderId),cancellationToken))
            throw new DomainException("AUCTION_SELLER_RESULT_ALREADY_NOTIFIED","Auction result already has a seller notification.",409);
        var code = winnerSelected ? "AUCTION_SELLER_WINNER_SELECTED_PUSH" : "AUCTION_SELLER_NO_BIDS_PUSH"; var locale = "vi-VN";
        var template = await db.NotificationTemplates.FromSqlInterpolated(
            $"SELECT * FROM \"NotificationTemplates\" WHERE \"Code\" = {code} AND \"Locale\" = {locale} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(t => t.IsActive,cancellationToken);
        if (template is null) throw new DomainException("TEMPLATE_NOT_FOUND","Active seller auction result template was not found.",404);
        if (template.ChannelCode != ChannelType.Push || template.NotificationType != NotificationType.AuctionSellerResult)
            throw new DomainException("AUCTION_SELLER_RESULT_TEMPLATE_INVALID","Seller result template must use the auction seller result type and push channel.",409);
        var channels = await db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted && (c.Code == ChannelType.InApp || c.Code == ChannelType.Push)).ToListAsync(cancellationToken);
        if (channels.Count != 2) throw new DomainException("CHANNEL_NOT_FOUND","Push and in-app channel configurations are required.",409);
        var mandatory = await NotificationPreferencePolicy.MandatoryPairsAsync(db,cancellationToken);
        var preferences = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == target && p.NotificationType == NotificationType.AuctionSellerResult && !p.IsDeleted && p.IsDisabled)
            .Select(p => p.ChannelCode).ToListAsync(cancellationToken);
        string? Policy(ChannelType channel) => channels.Single(c => c.Code == channel).IsDisabled ? "CHANNEL_DISABLED" :
            !mandatory.Contains((NotificationType.AuctionSellerResult,channel)) && preferences.Contains(channel) ? "PREFERENCE_DISABLED" : null;
        var inAppError = Policy(ChannelType.InApp); var pushError = Policy(ChannelType.Push);
        var devices = pushError is not null ? new List<Guid>() : await db.DeviceTokens.AsNoTracking().Where(d => d.UserId == target && d.IsActive && !d.IsDeleted)
            .OrderBy(d => d.Id).Select(d => d.Id).Take(101).ToListAsync(cancellationToken);
        pushError ??= devices.Count == 0 ? "PUSH_NO_ACTIVE_DEVICES" : devices.Count > 100 ? "PUSH_DEVICE_LIMIT" : null;
        var now = Utc(time.GetUtcNow()); var visible = inAppError is null; var hidden = !visible && pushError is not null;
        var data = new Dictionary<string,string> { ["AuctionTitle"] = title,["DecidedAtUtc"] = decided.ToString("yyyy-MM-dd HH:mm:ss 'UTC'",CultureInfo.InvariantCulture) };
        if (winnerSelected)
        {
            data["WinningAmount"] = r.WinningAmount!.Value.ToString("N2",CultureInfo.InvariantCulture);
            data["PaymentDeadlineUtc"] = deadline!.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'",CultureInfo.InvariantCulture);
        }
        var row = new NotificationRecord
        {
            Id = Guid.NewGuid(),UserId = target,SourceEventId = eventId,TemplateId = template.Id,NotificationType = NotificationType.AuctionSellerResult,ChannelScope = ChannelScope.All,
            Title = TemplateRenderer.Render(template.SubjectTemplate,data,false,500),Body = TemplateRenderer.Render(template.BodyTemplate,data,false,4000),
            Payload = JsonSerializer.Serialize(new { type = "AuctionSellerResult",outcome,auctionId = auction,resultId = result,winningBidId = r.WinningBidId,orderId = r.OrderId,
                winningAmount = r.WinningAmount,currency = winnerSelected ? "VND" : null,decidedAt = decided,paymentDeadline = deadline }),
            IsInboxVisible = visible,IsRead = !visible,ReadAt = visible ? null : now,IsDeleted = hidden,DeletedAt = hidden ? now : null,
            Status = pushError is null ? NotificationStatus.Pending : NotificationStatus.Completed,CreatedAt = now,UpdatedAt = now
        };
        db.Notifications.Add(row);
        AddDelivery(row,ChannelType.InApp,target.ToString(),visible ? DeliveryStatus.Sent : DeliveryStatus.Skipped,inAppError,now);
        if (pushError is not null) AddDelivery(row,ChannelType.Push,target.ToString(),DeliveryStatus.Skipped,pushError,now);
        else foreach (var device in devices) AddDelivery(row,ChannelType.Push,device.ToString(),DeliveryStatus.Pending,null,now);
        var receipt = new AuctionSellerResultDispatch { Id = eventId,AuctionId = auction,ResultId = result,WinningBidId = r.WinningBidId,OrderId = r.OrderId,NotificationId = row.Id,RequestHash = hash,CreatedAt = now,UpdatedAt = now };
        db.AuctionSellerResultDispatches.Add(receipt);
        db.AddAuditLog(AuditAction.AuctionSellerResultAccepted,nameof(AuctionSellerResultDispatch),eventId.ToString(),newValues:new
        { SourceEventId = eventId,AuctionId = auction,ResultId = result,SellerUserId = target,Outcome = outcome,NotificationId = row.Id },traceId:r.TraceId);
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
            Level = Notification.Domain.Enums.LogLevel.Info,Message = "Seller auction result delivery recorded.",Metadata = JsonSerializer.Serialize(new { d.ErrorCode }),CreatedAt = now,UpdatedAt = now });
    }
    private async Task<AuctionSellerResultResponse> Response(AuctionSellerResultDispatch r,bool replay,CancellationToken ct)
    {
        var row = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.NotificationId,ct);
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().Where(x => x.NotificationId == r.NotificationId).OrderBy(x => x.ChannelCode).ThenBy(x => x.Id).ToListAsync(ct);
        if (row is null || deliveries.Count == 0) throw new DomainException("DELIVERY_STATE_INVALID","Seller auction result delivery state is invalid.",409);
        return new(r.Id,r.AuctionId,r.ResultId,row.Id,row.UserId,row.Status.ToString(),row.IsInboxVisible && !row.IsDeleted,replay,
            deliveries.Select(d => new AuctionSellerResultDeliveryResponse(d.Id,d.ChannelCode.ToString(),d.Status.ToString(),d.ErrorCode)).ToList());
    }
    public async Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default)
    {
        var row = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId && !n.IsDeleted &&
            db.AuctionSellerResultDispatches.IgnoreQueryFilters().Any(r => r.NotificationId == n.Id),cancellationToken);
        if (row is null) throw new DomainException("NOTIFICATION_NOT_FOUND","Seller result notification was not found or does not belong to the user.",404);
        return new(row.Id,row.UserId,row.Title,row.Body,row.Payload,row.IsRead,row.ReadAt,row.NotificationType.ToString(),row.Status.ToString(),row.CreatedAt);
    }
}
