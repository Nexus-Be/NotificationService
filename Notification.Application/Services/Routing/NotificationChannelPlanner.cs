namespace Notification.Application.Services.Routing;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Application.Services.Notifications;
using Notification.Domain.Entities;
using Notification.Domain.Enums;

/// <summary>Plans channels only for newly accepted business notifications, in their database transaction.</summary>
public static class NotificationChannelPlanner
{
    public static bool IsImportant(NotificationType type, string? payload = null)
    {
        if (type == NotificationType.OrderShipped)
        {
            try { using var json = JsonDocument.Parse(payload ?? "{}");
                return json.RootElement.TryGetProperty("status", out var status) &&
                    status.GetString()?.Replace("_", "").ToUpperInvariant() is "DELIVERED" or "DELIVERYFAILED" or "RETURNING" or "RETURNED" or "CANCELLED";
            } catch (JsonException) { return false; }
        }
        return type is NotificationType.SecurityAlert or NotificationType.ReputationPenalty or NotificationType.OrderPaid
            or NotificationType.OrderDecision or NotificationType.OrderRefunded or NotificationType.OrderCompleted
            or NotificationType.OrderSellerSettled or NotificationType.OrderPaymentExpired or NotificationType.OrderBuyerCancelled
            or NotificationType.AuctionWon or NotificationType.AuctionCancelled or NotificationType.AuctionPaymentTimedOut
            or NotificationType.AuctionSellerResult or NotificationType.AuctionFailed or NotificationType.AuctionWinnerOffer
            or NotificationType.AuctionReserveApprovalNeeded or NotificationType.AuctionReserveRestriction
            or NotificationType.AuctionDeposit or NotificationType.WithdrawalResult
            or NotificationType.ProductBannedByReport or NotificationType.ProductBanned or NotificationType.ProductSuspended or NotificationType.ProductRejected;
    }

    public static async Task ApplyAsync(INotificationDbContext db, IReadOnlyList<NotificationRecord> records,
        List<NotificationDelivery> added, CancellationToken ct, bool enablePush = false)
    {
        if (records.Count == 0) return;
        var channels = await db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted).ToListAsync(ct);
        var mandatoryPairs = await NotificationPreferencePolicy.MandatoryPairsAsync(db, ct);
        var templateIds = records.Where(n => n.TemplateId.HasValue).Select(n => n.TemplateId!.Value).Distinct().ToList();
        var htmlTemplates = templateIds.Count == 0 ? new List<Guid>() : await db.NotificationTemplates.AsNoTracking()
            .Where(t => templateIds.Contains(t.Id) && t.ChannelCode == ChannelType.Email).Select(t => t.Id).ToListAsync(ct);
        foreach (var row in records)
        {
            if (row.NotificationType is not { } type || type == NotificationType.Otp || row.UserId == Guid.Empty) continue;
            var important = IsImportant(type, row.Payload);
            var deliveries = added.Where(d => d.NotificationId == row.Id).ToList();
            var now = row.CreatedAt;
            // Preserve the rendered HTML before making inbox content plain text.
            foreach (var email in deliveries.Where(d => d.ChannelCode == ChannelType.Email))
            { email.SubjectOverride ??= row.Title; email.BodyOverride ??= row.Body; }
            var plain = row.TemplateId is { } templateId && htmlTemplates.Contains(templateId)
                ? WebUtility.HtmlDecode(Regex.Replace(row.Body ?? "", "<[^>]+>", " ")) : row.Body ?? "";
            row.Body = plain.Length > 4000 ? plain[..4000] : plain;
            bool Enabled(ChannelType channel) => channels.Any(c => c.Code == channel && !c.IsDisabled);
            var optionalInboxDisabled = !NotificationPreferencePolicy.IsCritical(type) && !mandatoryPairs.Contains((type, ChannelType.InApp)) && await db.NotificationPreferences.AsNoTracking().AnyAsync(p =>
                p.UserId == row.UserId && p.NotificationType == type && p.ChannelCode == ChannelType.InApp && p.IsDisabled && !p.IsDeleted, ct);
            var inbox = Enabled(ChannelType.InApp) && !optionalInboxDisabled;
            var existingInbox = deliveries.SingleOrDefault(d => d.ChannelCode == ChannelType.InApp);
            if (existingInbox is null) Add(ChannelType.InApp, row.UserId.ToString(), inbox ? DeliveryStatus.Sent : DeliveryStatus.Skipped,
                inbox ? null : "CHANNEL_DISABLED");
            else
            { existingInbox.Status = inbox ? DeliveryStatus.Sent : DeliveryStatus.Skipped; existingInbox.ErrorCode = inbox ? null : "CHANNEL_DISABLED"; }
            row.IsInboxVisible = inbox; row.IsRead = !inbox; row.ReadAt = inbox ? null : now;
            if (important && !deliveries.Any(d => d.ChannelCode == ChannelType.Email))
            {
                var optedOut = !NotificationPreferencePolicy.IsCritical(type) && !mandatoryPairs.Contains((type, ChannelType.Email)) &&
                    await db.NotificationPreferences.AsNoTracking().AnyAsync(p => p.UserId == row.UserId && p.NotificationType == type && p.ChannelCode == ChannelType.Email && p.IsDisabled && !p.IsDeleted, ct);
                var email = Add(ChannelType.Email, "user:" + row.UserId, Enabled(ChannelType.Email) && !optedOut ? DeliveryStatus.Pending : DeliveryStatus.Skipped,
                    !Enabled(ChannelType.Email) ? "CHANNEL_DISABLED" : optedOut ? "PREFERENCE_DISABLED" : null);
                email.SubjectOverride = row.Title ?? "Thông báo từ Nexus";
                // 4,000-character persisted limit also applies after HTML escaping.
                var excerpt = row.Body.Length > 400 ? row.Body[..400] + "…" : row.Body;
                email.BodyOverride = "<!doctype html><html lang='vi'><body style='margin:0;background:#f3f6fa;font-family:Arial,sans-serif;color:#182230'>" +
                    "<main style='max-width:600px;margin:32px auto;background:white;border-radius:12px;padding:28px'><p style='color:#0086c9;font-weight:bold'>NEXUS</p>" +
                    "<h2>" + WebUtility.HtmlEncode((row.Title ?? "Thông báo")[..Math.Min(row.Title?.Length ?? 9, 100)]) + "</h2><p>" + WebUtility.HtmlEncode(excerpt) +
                    "</p><p>Vui lòng đăng nhập Nexus để xem thông tin chi tiết.</p><hr><p style='font-size:12px;color:#667085'>Email tự động từ Nexus. Không cung cấp mật khẩu hoặc mã OTP cho người khác.</p></main></body></html>";
            }
            if (!important)
                foreach (var delivery in deliveries.Where(d => d.ChannelCode is ChannelType.Email or ChannelType.Push))
                { delivery.Status = DeliveryStatus.Skipped; delivery.ErrorCode = "IN_APP_ONLY"; delivery.ErrorMessage = "This event uses in-app notifications only."; }
            if (important && !enablePush)
                foreach (var delivery in deliveries.Where(d => d.ChannelCode == ChannelType.Push))
                { delivery.Status = DeliveryStatus.Skipped; delivery.ErrorCode = "EMAIL_IN_APP_ONLY"; delivery.ErrorMessage = "This deployment uses email and in-app notifications."; }

            row.IsDeleted = !inbox && !deliveries.Any(d => d.Status is DeliveryStatus.Pending or DeliveryStatus.Sending);
            row.DeletedAt = row.IsDeleted ? now : null;
            row.Status = deliveries.Any(d => d.Status is DeliveryStatus.Pending or DeliveryStatus.Sending) ? NotificationStatus.Pending : NotificationStatus.Completed;
            row.ChannelScope = important ? ChannelScope.All : ChannelScope.Specific;

            NotificationDelivery Add(ChannelType channel, string destination, DeliveryStatus status, string? error)
            {
                var delivery = new NotificationDelivery { Id = Guid.NewGuid(), NotificationId = row.Id, ChannelCode = channel,
                    Destination = destination, Status = status, ErrorCode = error, RowVersion = 1, CreatedAt = now, UpdatedAt = now,
                    AttemptCount = status == DeliveryStatus.Sent ? 1 : 0, LastAttemptAt = status == DeliveryStatus.Sent ? now : null };
                db.NotificationDeliveries.Add(delivery); added.Add(delivery); deliveries.Add(delivery); return delivery;
            }
        }
    }
}
