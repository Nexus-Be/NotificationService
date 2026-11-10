namespace Notification.Application.Services.AccountStatusChanges;

using System.ComponentModel.DataAnnotations;
using System.Globalization;
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

public sealed class AccountStatusChangeService(INotificationDbContext db, IDispatchService dispatch, TimeProvider time) : IAccountStatusChangeService
{
    public async Task<AccountStatusChangeResponse> CreateAsync(CreateAccountStatusChangeRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null ||
            request.SourceEventId is null ||
            request.SourceEventId == Guid.Empty ||
            request.UserId is null ||
            request.UserId == Guid.Empty ||
            request.StatusVersion is null or < 1 ||
            string.IsNullOrWhiteSpace(request.Email) ||
            request.Email.Length > 254 ||
            request.Email.Any(char.IsControl) ||
            !new EmailAddressAttribute().IsValid(request.Email.Trim()) ||
            request.FullName?.Length > 200 ||
            request.FullName?.Any(char.IsControl) == true ||
            request.TraceId?.Length > 100 ||
            request.TraceId?.Any(char.IsControl) == true ||
            string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Length > 1000 ||
            request.Reason.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) ||
            request.ChangedAt is null ||
            request.ChangedAt.Value > time.GetUtcNow().AddSeconds(30))
            throw new DomainException("VAL_INVALID_INPUT",
                                      "Account status notification fields are invalid.");

        var change = request.Change?.Trim().ToLowerInvariant();

        if (change is not ("banned" or "unbanned"))
            throw new DomainException("VAL_INVALID_INPUT",
                                       "Account status change must be banned or unbanned.");
        var eventId = request.SourceEventId.Value; var userId = request.UserId.Value; var version = request.StatusVersion.Value;
        var email = request.Email.Trim().ToLowerInvariant();
        var name = string.IsNullOrWhiteSpace(request.FullName) ? "Customer" : request.FullName.Trim(); var reason = request.Reason.Trim();
        var changedAt = new DateTimeOffset(request.ChangedAt.Value.ToUniversalTime().Ticks / 10 * 10, TimeSpan.Zero);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { userId, version, email, name, change, reason, changedAt }))));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Lock("account-status-event:" + eventId.ToString("N"), cancellationToken);
        var existing = await db.AccountStatusChangeDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != hash) throw Conflict();
            return await Response(existing, cancellationToken);
        }
        await Lock("account-status-version:" + userId.ToString("N") + ":" + version.ToString(CultureInfo.InvariantCulture), cancellationToken);
        if (await db.AccountStatusChangeDispatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.UserId == userId && x.StatusVersion == version, cancellationToken))
            throw new DomainException("ACCOUNT_STATUS_ALREADY_NOTIFIED", "Account status revision already has a notification.", 409);
        var code = change == "banned" ? "ACCOUNT_BANNED_EMAIL" : "ACCOUNT_UNBANNED_EMAIL"; var locale = "vi-VN";
        var template = await db.NotificationTemplates.FromSqlInterpolated(
            $"SELECT * FROM \"NotificationTemplates\" WHERE \"Code\" = {code} AND \"Locale\" = {locale} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(t => t.IsActive, cancellationToken);
        if (template is null) throw new DomainException("TEMPLATE_NOT_FOUND", "Active notification template was not found.", 404);
        if (template.ChannelCode != ChannelType.Email || template.NotificationType != NotificationType.SecurityAlert || !template.IsMandatory)
            throw new DomainException("ACCOUNT_STATUS_TEMPLATE_INVALID", "Account status template must be mandatory security email.", 409);
        var created = await dispatch.CreateAsync(new CreateDispatchRequest
        {
            SourceEventId = eventId,
            UserId = userId,
            TemplateCode = code,
            Locale = locale,
            Destination = email,
            Data = new() { ["FullName"] = name, ["Reason"] = reason, ["ChangedAtUtc"] = changedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) }
        }, cancellationToken);
        if (created.IsReplay) throw Conflict();
        var now = time.GetUtcNow();
        db.AccountStatusChangeDispatches.Add(new()
        { Id = eventId, UserId = userId, StatusVersion = version, RequestHash = hash, NotificationId = created.NotificationId, CreatedAt = now, UpdatedAt = now });
        db.AddAuditLog(AuditAction.AccountStatusChangeAccepted, nameof(AccountStatusChangeDispatch), eventId.ToString(), newValues: new
        { SourceEventId = eventId, UserId = userId, StatusVersion = version, Change = change, ChangedAt = changedAt, created.NotificationId, created.DeliveryId, created.DeliveryStatus },
            userId: userId.ToString(), traceId: request.TraceId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(eventId, userId, version, created.NotificationId, created.DeliveryId, created.NotificationStatus, created.DeliveryStatus, false);
    }
    private Task<int> Lock(string key, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8012))", ct);
    private static DomainException Conflict() => new("ACCOUNT_STATUS_CONFLICT", "Account status event ID was already used for different fields or an unmatched generic dispatch.", 409);
    private async Task<AccountStatusChangeResponse> Response(AccountStatusChangeDispatch receipt, CancellationToken ct)
    {
        var notification = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == receipt.NotificationId, ct);
        var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().Where(x => x.NotificationId == receipt.NotificationId).ToListAsync(ct);
        if (notification is null || deliveries.Count != 1 || deliveries[0].ChannelCode != ChannelType.Email)
            throw new DomainException("DELIVERY_STATE_INVALID", "Account status delivery state is invalid.", 409);
        return new(receipt.Id, receipt.UserId, receipt.StatusVersion, receipt.NotificationId, deliveries[0].Id, notification.Status.ToString(), deliveries[0].Status.ToString(), true);
    }
}
