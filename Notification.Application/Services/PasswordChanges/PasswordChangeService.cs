namespace Notification.Application.Services.PasswordChanges;

using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Common.Extensions;
using Notification.Application.Interfaces;
using Notification.Application.Services.Dispatches;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

public sealed class PasswordChangeService(INotificationDbContext db,IDispatchService dispatch,TimeProvider time) : IPasswordChangeService
{
    public async Task<PasswordChangeResponse> CreateAsync(CreatePasswordChangeRequest request,CancellationToken cancellationToken = default)
    {
        if (request is null || request.SourceEventId is null || request.SourceEventId == Guid.Empty ||
            request.UserId is null || request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 ||
            !new EmailAddressAttribute().IsValid(request.Email.Trim()) || request.Email.Any(char.IsControl) ||
            request.FullName?.Length > 200 || request.FullName?.Any(char.IsControl) == true ||
            request.TraceId?.Length > 100 || request.TraceId?.Any(char.IsControl) == true ||
            request.ChangedAt is null || request.ChangedAt.Value > time.GetUtcNow().AddSeconds(30))
            throw new DomainException("VAL_INVALID_INPUT","Password change notification fields are invalid.");
        var kind = request.Kind?.Trim().ToLowerInvariant();
        if (kind is not ("changed" or "reset")) throw new DomainException("VAL_INVALID_INPUT","Password change kind must be changed or reset.");
        var email = request.Email.Trim().ToLowerInvariant();
        var name = string.IsNullOrWhiteSpace(request.FullName) ? "Customer" : request.FullName.Trim();
        var changedAt = new DateTimeOffset(request.ChangedAt.Value.ToUniversalTime().Ticks / 10 * 10,TimeSpan.Zero);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { request.UserId,email,name,kind,changedAt }))));
        var eventId = request.SourceEventId.Value;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = "password-change:" + eventId.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8010))",cancellationToken);
        var existing = await db.PasswordChangeDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId,cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != hash) throw new DomainException("PASSWORD_CHANGE_CONFLICT","Password change event ID was already used for different fields.",409);
            return await Response(existing,true,cancellationToken);
        }
        var code = kind == "changed" ? "PASSWORD_CHANGED_EMAIL" : "PASSWORD_RESET_SUCCEEDED_EMAIL";
        // Keep template identity/type/channel stable while the shared dispatch service rereads it.
        var locale = "vi-VN";
        var template = await db.NotificationTemplates.FromSqlInterpolated(
            $"SELECT * FROM \"NotificationTemplates\" WHERE \"Code\" = {code} AND \"Locale\" = {locale} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(t => t.IsActive,cancellationToken);
        if (template is null) throw new DomainException("TEMPLATE_NOT_FOUND","Active notification template was not found.",404);
        if (template.ChannelCode != ChannelType.Email || template.NotificationType != NotificationType.SecurityAlert || !template.IsMandatory)
            throw new DomainException("PASSWORD_CHANGE_TEMPLATE_INVALID","Password change template must be mandatory security email.",409);
        var created = await dispatch.CreateAsync(new CreateDispatchRequest
        {
            SourceEventId = eventId,UserId = request.UserId,TemplateCode = code,Locale = "vi-VN",Destination = email,
            Data = new() { ["FullName"] = name,["ChangedAtUtc"] = changedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'",System.Globalization.CultureInfo.InvariantCulture) }
        },cancellationToken);
        // A generic dispatch has no canonical business receipt; do not adopt unknown content.
        if (created.IsReplay) throw new DomainException("PASSWORD_CHANGE_CONFLICT","Password change event was already dispatched without a matching business receipt.",409);
        var now = time.GetUtcNow();
        var receipt = new PasswordChangeDispatch { Id = eventId,RequestHash = hash,NotificationId = created.NotificationId,CreatedAt = now,UpdatedAt = now };
        db.PasswordChangeDispatches.Add(receipt);
        db.AddAuditLog(AuditAction.PasswordChangeAccepted,nameof(PasswordChangeDispatch),eventId.ToString(),newValues:new
        { SourceEventId = eventId,request.UserId,Kind = kind,ChangedAt = changedAt,created.NotificationId,created.DeliveryId,created.DeliveryStatus },
            userId:request.UserId.Value.ToString(),traceId:request.TraceId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(eventId,created.NotificationId,created.DeliveryId,created.NotificationStatus,created.DeliveryStatus,false);
    }

    private async Task<PasswordChangeResponse> Response(PasswordChangeDispatch receipt,bool replay,CancellationToken ct)
    {
        var notification = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == receipt.NotificationId,ct);
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().Where(x => x.NotificationId == receipt.NotificationId).ToListAsync(ct);
        if (notification is null || deliveries.Count != 1 || deliveries[0].ChannelCode != ChannelType.Email)
            throw new DomainException("DELIVERY_STATE_INVALID","Password change delivery state is invalid.",409);
        return new(receipt.Id,receipt.NotificationId,deliveries[0].Id,notification.Status.ToString(),deliveries[0].Status.ToString(),replay);
    }
}
