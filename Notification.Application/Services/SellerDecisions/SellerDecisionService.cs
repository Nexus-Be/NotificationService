namespace Notification.Application.Services.SellerDecisions;

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

public sealed class SellerDecisionService(INotificationDbContext db, IDispatchService dispatch, TimeProvider time) : ISellerDecisionService
{
    private static DomainException Invalid() => new("VAL_INVALID_INPUT",
                                                    "Seller decision notification fields are invalid.");

    public async Task<SellerDecisionResponse> CreateAsync(CreateSellerDecisionRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null ||
            request.SourceEventId is null ||
            request.SourceEventId == Guid.Empty ||
            request.ApplicationId is null ||
            request.ApplicationId == Guid.Empty ||
            request.UserId is null ||
            request.UserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Email) ||
            request.Email.Length > 254 ||
            request.Email.Any(char.IsControl) ||
            !new EmailAddressAttribute().IsValid(request.Email.Trim()) ||
            string.IsNullOrWhiteSpace(request.ShopName) ||
            request.ShopName.Length > 200 ||
            request.ShopName.Any(char.IsControl) ||
            request.FullName?.Length > 200 ||
            request.FullName?.Any(char.IsControl) == true ||
            request.TraceId?.Length > 100 ||
            request.TraceId?.Any(char.IsControl) == true ||
            request.RejectionReason?.Length > 1000 ||
            request.RejectionReason?.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) == true ||
            request.DecidedAt is null ||
            request.DecidedAt.Value > time.GetUtcNow().AddSeconds(30))
            throw Invalid();

        var decision = request.Decision?.Trim().ToLowerInvariant();
        var reason = string.IsNullOrWhiteSpace(request.RejectionReason) ? null : request.RejectionReason.Trim();

        if (decision is not ("approved" or "rejected") || (decision == "rejected" && reason is null) || (decision == "approved" && reason is not null))
            throw Invalid();

        var eventId = request.SourceEventId.Value;
        var applicationId = request.ApplicationId.Value;
        var email = request.Email.Trim().ToLowerInvariant();
        var name = string.IsNullOrWhiteSpace(request.FullName) ? "Customer" : request.FullName.Trim();
        var shop = request.ShopName.Trim();
        var decidedAt = new DateTimeOffset(request.DecidedAt.Value.ToUniversalTime().Ticks / 10 * 10, TimeSpan.Zero);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { applicationId, request.UserId, email, name, shop, decision, reason, decidedAt }))));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Lock("seller-event:" + eventId.ToString("N"), cancellationToken);

        var existing = await db.SellerDecisionDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);

        if (existing is not null)
        {
            if (existing.RequestHash != hash)
                throw Conflict();
            return await Response(existing, cancellationToken);
        }

        // A reviewed application has one final outcome; aliases must not create another email.
        await Lock("seller-application:" + applicationId.ToString("N"), cancellationToken);

        if (await db.SellerDecisionDispatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.ApplicationId == applicationId, cancellationToken))
            throw new DomainException("SELLER_APPLICATION_ALREADY_NOTIFIED",
                                      "Seller application already has a notification for its final decision.", 409);

        var code = decision == "approved" ? "SELLER_APPROVED_EMAIL" : "SELLER_REJECTED_EMAIL";
        var locale = "vi-VN";
        var template = await db.NotificationTemplates.FromSqlInterpolated(
            $"SELECT * FROM \"NotificationTemplates\" WHERE \"Code\" = {code} AND \"Locale\" = {locale} FOR SHARE")
                .AsNoTracking().SingleOrDefaultAsync(t => t.IsActive, cancellationToken);

        if (template is null)
            throw new DomainException("TEMPLATE_NOT_FOUND",
                                      "Active notification template was not found.", 404);

        if (template.ChannelCode != ChannelType.Email || template.NotificationType != NotificationType.SecurityAlert || !template.IsMandatory)
            throw new DomainException("SELLER_DECISION_TEMPLATE_INVALID",
                                      "Seller decision template must be mandatory account security email.", 409);

        var data = new Dictionary<string, string>
        {
            ["FullName"] = name,
            ["ShopName"] = shop,
            ["DecidedAtUtc"] = decidedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'",
                                                   CultureInfo.InvariantCulture)
        };

        if (reason is not null)
            data["RejectionReason"] = reason;

        var created = await dispatch.CreateAsync(
            new CreateDispatchRequest
            {
                SourceEventId = eventId,
                UserId = request.UserId,
                TemplateCode = code,
                Locale = locale,
                Destination = email,
                Data = data
            },
                                      cancellationToken);

        if (created.IsReplay)
            throw Conflict();

        var now = time.GetUtcNow();

        db.SellerDecisionDispatches.Add(new()
        {
            Id = eventId,
            ApplicationId = applicationId,
            RequestHash = hash,
            NotificationId = created.NotificationId,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.AddAuditLog(AuditAction.SellerDecisionAccepted, nameof(SellerDecisionDispatch), eventId.ToString(), newValues: new
        {
            SourceEventId = eventId,
            ApplicationId = applicationId,
            request.UserId,
            Decision = decision,
            DecidedAt = decidedAt,
            created.NotificationId,
            created.DeliveryId,
            created.DeliveryStatus
        },
        userId: request.UserId.Value.ToString(), traceId: request.TraceId);

        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);

        return new(eventId, applicationId, created.NotificationId, created.DeliveryId, created.NotificationStatus, created.DeliveryStatus, false);
    }

    private Task<int> Lock(string key, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8011))", ct);

    private static DomainException Conflict() => new("SELLER_DECISION_CONFLICT",
                                                     "Seller decision event ID was already used for different fields or an unmatched generic dispatch.", 409);

    private async Task<SellerDecisionResponse> Response(SellerDecisionDispatch receipt, CancellationToken ct)
    {
        var notification = await db.Notifications
                                     .IgnoreQueryFilters()
                                     .AsNoTracking()
                                     .SingleOrDefaultAsync(x => x.Id == receipt
                                     .NotificationId, ct);

        var deliveries = await db.NotificationDeliveries
                                    .IgnoreQueryFilters()
                                    .AsNoTracking().Where(x => x
                                    .NotificationId == receipt
                                    .NotificationId)
                                    .ToListAsync(ct);

        if (notification is null || deliveries.Count != 1 || deliveries[0].ChannelCode != ChannelType.Email)
            throw new DomainException("DELIVERY_STATE_INVALID",
                                      "Seller decision delivery state is invalid.", 409);
        return new(receipt.Id,
                   receipt.ApplicationId,
                   receipt.NotificationId,
                   deliveries[0].Id,
                   notification.Status.ToString(),
                   deliveries[0].Status.ToString(),
                   true);
    }
}
