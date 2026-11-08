namespace Notification.Application.Services.AuctionWinners;
using System.ComponentModel.DataAnnotations;
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

public sealed class AuctionWinnerService(INotificationDbContext db,TimeProvider time) : IAuctionWinnerService
{
    private static DateTimeOffset Utc(DateTimeOffset value) => new(value.ToUniversalTime().Ticks / 10 * 10,TimeSpan.Zero);
    private static bool Id(Guid? value) => value.HasValue && value != Guid.Empty;
    private Task<int> Lock(string key,CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8015))",ct);
    private static DomainException Conflict() => new("AUCTION_WINNER_CONFLICT","Winner event conflicts with an existing request or dispatch.",409);
    public async Task<AuctionWinnerResponse> CreateAsync(CreateAuctionWinnerRequest r,CancellationToken cancellationToken = default)
    {
        if (r is null || !Id(r.SourceEventId) || !Id(r.AuctionId) || !Id(r.ResultId) || !Id(r.WinningBidId) || !Id(r.WinnerUserId) || !Id(r.SellerUserId) ||
            r.WinnerUserId == r.SellerUserId || r.OrderId == Guid.Empty || string.IsNullOrWhiteSpace(r.WinnerEmail) || r.WinnerEmail.Length > 254 ||
            r.WinnerEmail.Any(char.IsControl) || !new EmailAddressAttribute().IsValid(r.WinnerEmail.Trim()) || r.WinnerFullName?.Length > 200 || r.WinnerFullName?.Any(char.IsControl) == true ||
            string.IsNullOrWhiteSpace(r.AuctionTitle) || r.AuctionTitle.Length > 200 || r.AuctionTitle.Any(char.IsControl) || r.WinningAmount is null or <= 0 or > 9999999999999999.99m ||
            decimal.Round(r.WinningAmount.Value,2) != r.WinningAmount || !string.Equals(r.Currency,"VND",StringComparison.OrdinalIgnoreCase) ||
            r.SettledAt is null || r.SettledAt > time.GetUtcNow().AddSeconds(30) || r.PaymentDeadline is null || Utc(r.PaymentDeadline.Value) <= Utc(r.SettledAt.Value) ||
            r.TraceId?.Length > 100 || r.TraceId?.Any(char.IsControl) == true)
            throw new DomainException("VAL_INVALID_INPUT","Auction winner notification fields are invalid.");
        var id = r.SourceEventId!.Value; var auction = r.AuctionId!.Value; var result = r.ResultId!.Value; var bid = r.WinningBidId!.Value; var user = r.WinnerUserId!.Value;
        var email = r.WinnerEmail.Trim().ToLowerInvariant(); var name = string.IsNullOrWhiteSpace(r.WinnerFullName) ? "Customer" : r.WinnerFullName.Trim(); var title = r.AuctionTitle.Trim();
        var settled = Utc(r.SettledAt.Value); var deadline = Utc(r.PaymentDeadline.Value);
        var proof = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { auction,result,bid,user,r.SellerUserId,r.OrderId,email,name,title,Amount = r.WinningAmount.Value.ToString("0.00",CultureInfo.InvariantCulture),Currency = "VND",settled,deadline }))));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await Lock("winner-event:" + id.ToString("N"),cancellationToken);
        var old = await db.AuctionWinnerDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id,cancellationToken);
        if (old is not null) { if (old.RequestHash != proof) throw Conflict(); return await Response(old,true,cancellationToken); }
        await Lock("winner-auction:" + auction.ToString("N"),cancellationToken);
        await Lock("winner-result:" + result.ToString("N"),cancellationToken);
        await Lock("winner-bid:" + bid.ToString("N"),cancellationToken);
        if (await db.AuctionWinnerDispatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.AuctionId == auction || x.ResultId == result || x.WinningBidId == bid,cancellationToken))
            throw new DomainException("AUCTION_RESULT_ALREADY_NOTIFIED","Auction result already has a winner notification.",409);
        var codes = new[] { "AUCTION_WINNER_EMAIL","AUCTION_WINNER_PUSH" }; var locale = "vi-VN";
        var templates = await db.NotificationTemplates.FromSqlInterpolated(
            $"SELECT * FROM \"NotificationTemplates\" WHERE \"Code\" = ANY ({codes}) AND \"Locale\" = {locale} AND \"IsActive\" = TRUE AND \"IsDeleted\" = FALSE ORDER BY \"Id\" FOR SHARE")
            .AsNoTracking().ToListAsync(cancellationToken);
        if (templates.Count != 2) throw new DomainException("TEMPLATE_NOT_FOUND","Active auction winner templates were not found.",404);
        var emailTemplate = templates.Single(t => t.Code == codes[0]); var pushTemplate = templates.Single(t => t.Code == codes[1]);
        if (templates.Any(t => t.NotificationType != NotificationType.AuctionWon) || emailTemplate.ChannelCode != ChannelType.Email || !emailTemplate.IsMandatory || pushTemplate.ChannelCode != ChannelType.Push)
            throw new DomainException("AUCTION_WINNER_TEMPLATE_INVALID","Winner templates must use auction won type, mandatory email and push channels.",409);
        var channels = await db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted && (c.Code == ChannelType.InApp || c.Code == ChannelType.Push || c.Code == ChannelType.Email)).ToListAsync(cancellationToken);
        if (channels.Count != 3) throw new DomainException("CHANNEL_NOT_FOUND","Email, push and in-app channel configurations are required.",409);
        var mandatory = await NotificationPreferencePolicy.MandatoryPairsAsync(db,cancellationToken);
        var preferences = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == user && p.NotificationType == NotificationType.AuctionWon && p.IsDisabled && !p.IsDeleted)
            .Select(p => p.ChannelCode).ToListAsync(cancellationToken);
        string? Policy(ChannelType c) => channels.Single(x => x.Code == c).IsDisabled ? "CHANNEL_DISABLED" :
            !mandatory.Contains((NotificationType.AuctionWon,c)) && preferences.Contains(c) ? "PREFERENCE_DISABLED" : null;
        var inAppError = Policy(ChannelType.InApp); var emailError = Policy(ChannelType.Email); var pushError = Policy(ChannelType.Push);
        var devices = pushError is not null ? new List<Guid>() : await db.DeviceTokens.AsNoTracking().Where(d => d.UserId == user && d.IsActive && !d.IsDeleted)
            .OrderBy(d => d.Id).Select(d => d.Id).Take(101).ToListAsync(cancellationToken);
        pushError ??= devices.Count == 0 ? "PUSH_NO_ACTIVE_DEVICES" : devices.Count > 100 ? "PUSH_DEVICE_LIMIT" : null;
        var data = new Dictionary<string,string> { ["FullName"] = name,["AuctionTitle"] = title,["WinningAmount"] = r.WinningAmount.Value.ToString("N2",CultureInfo.InvariantCulture),
            ["SettledAtUtc"] = settled.ToString("yyyy-MM-dd HH:mm:ss 'UTC'",CultureInfo.InvariantCulture),["PaymentDeadlineUtc"] = deadline.ToString("yyyy-MM-dd HH:mm:ss 'UTC'",CultureInfo.InvariantCulture),
            ["OrderReference"] = r.OrderId?.ToString() ?? "Vui lòng kiểm tra đơn đấu giá trong ứng dụng Nexus; nếu đơn chưa xuất hiện, hãy kiểm tra lại." };
        var subject = TemplateRenderer.Render(emailTemplate.SubjectTemplate,data,false,500); var html = TemplateRenderer.Render(emailTemplate.BodyTemplate,data,true,4000);
        var now = Utc(time.GetUtcNow()); var visible = inAppError is null; var hidden = !visible && emailError is not null && pushError is not null;
        var row = new NotificationRecord
        {
            Id = Guid.NewGuid(),UserId = user,SourceEventId = id,TemplateId = pushTemplate.Id,NotificationType = NotificationType.AuctionWon,ChannelScope = ChannelScope.All,
            Title = TemplateRenderer.Render(pushTemplate.SubjectTemplate,data,false,500),Body = TemplateRenderer.Render(pushTemplate.BodyTemplate,data,false,4000),
            Payload = JsonSerializer.Serialize(new { type = "AuctionWon",auctionId = auction,resultId = result,winningBidId = bid,orderId = r.OrderId,
                winningAmount = r.WinningAmount.Value,currency = "VND",settledAt = settled,paymentDeadline = deadline }),
            IsInboxVisible = visible,IsRead = !visible,ReadAt = visible ? null : now,IsDeleted = hidden,DeletedAt = hidden ? now : null,
            Status = emailError is null || pushError is null ? NotificationStatus.Pending : NotificationStatus.Completed,CreatedAt = now,UpdatedAt = now
        };
        db.Notifications.Add(row);
        AddDelivery(row,ChannelType.InApp,user.ToString(),visible ? DeliveryStatus.Sent : DeliveryStatus.Skipped,inAppError,now);
        AddDelivery(row,ChannelType.Email,email,emailError is null ? DeliveryStatus.Pending : DeliveryStatus.Skipped,emailError,now,subject,html);
        if (pushError is not null) AddDelivery(row,ChannelType.Push,user.ToString(),DeliveryStatus.Skipped,pushError,now);
        else foreach (var device in devices) AddDelivery(row,ChannelType.Push,device.ToString(),DeliveryStatus.Pending,null,now);
        var receipt = new AuctionWinnerDispatch { Id = id,AuctionId = auction,ResultId = result,WinningBidId = bid,NotificationId = row.Id,RequestHash = proof,CreatedAt = now,UpdatedAt = now };
        db.AuctionWinnerDispatches.Add(receipt);
        db.AddAuditLog(AuditAction.AuctionWinnerAccepted,nameof(AuctionWinnerDispatch),id.ToString(),newValues:new
        { SourceEventId = id,AuctionId = auction,ResultId = result,WinningBidId = bid,WinnerUserId = user,NotificationId = row.Id },traceId:r.TraceId);
        try { await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" }) { throw Conflict(); }
        return await Response(receipt,false,cancellationToken);
    }
    private void AddDelivery(NotificationRecord parent,ChannelType channel,string destination,DeliveryStatus status,string? error,DateTimeOffset now,string? subject = null,string? body = null)
    {
        var d = new NotificationDelivery { Id = Guid.NewGuid(),NotificationId = parent.Id,ChannelCode = channel,Destination = destination,Status = status,SubjectOverride = subject,BodyOverride = body,
            AttemptCount = status == DeliveryStatus.Sent ? 1 : 0,LastAttemptAt = status == DeliveryStatus.Sent ? now : null,ErrorCode = error,RowVersion = 1,CreatedAt = now,UpdatedAt = now };
        db.NotificationDeliveries.Add(d);
        db.NotificationLogs.Add(new NotificationLog { Id = Guid.NewGuid(),NotificationId = parent.Id,DeliveryId = d.Id,
            Action = status == DeliveryStatus.Sent ? LogAction.Sent : status == DeliveryStatus.Skipped ? LogAction.Suppressed : LogAction.Created,
            Level = Notification.Domain.Enums.LogLevel.Info,Message = "Auction winner delivery recorded.",Metadata = JsonSerializer.Serialize(new { d.ErrorCode }),CreatedAt = now,UpdatedAt = now });
    }
    private async Task<AuctionWinnerResponse> Response(AuctionWinnerDispatch r,bool replay,CancellationToken ct)
    {
        var n = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.NotificationId,ct);
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().Where(x => x.NotificationId == r.NotificationId).OrderBy(x => x.ChannelCode).ThenBy(x => x.Id).ToListAsync(ct);
        if (n is null || deliveries.Count == 0) throw new DomainException("DELIVERY_STATE_INVALID","Auction winner delivery state is invalid.",409);
        return new(r.Id,r.AuctionId,r.ResultId,n.Id,n.UserId,n.Status.ToString(),n.IsInboxVisible && !n.IsDeleted,replay,deliveries.Select(d => new AuctionWinnerDeliveryResponse(d.Id,d.ChannelCode.ToString(),d.Status.ToString(),d.ErrorCode)).ToList());
    }
    public async Task<InAppNotificationItemResponse> GetNotificationAsync(Guid userId,Guid notificationId,CancellationToken cancellationToken = default)
    {
        var n = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId && !n.IsDeleted &&
            db.AuctionWinnerDispatches.IgnoreQueryFilters().Any(r => r.NotificationId == n.Id),cancellationToken);
        if (n is null) throw new DomainException("NOTIFICATION_NOT_FOUND","Winner notification was not found or does not belong to the user.",404);
        return new(n.Id,n.UserId,n.Title,n.Body,n.Payload,n.IsRead,n.ReadAt,n.NotificationType.ToString(),n.Status.ToString(),n.CreatedAt);
    }
}
