namespace Notification.Application.Services.Dispatches;

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

public sealed class DispatchService : IDispatchService
{
    private readonly INotificationDbContext _db;
    public DispatchService(INotificationDbContext db) => _db = db;

    public async Task<DispatchResponse> CreateAsync(CreateDispatchRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.SourceEventId is null || request.SourceEventId == Guid.Empty ||
            request.UserId is null || request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.TemplateCode) ||
            string.IsNullOrWhiteSpace(request.Locale) || request.Data is null || request.Data.Count > 50 ||
            request.Data.Any(x => x.Key.Length > 64 || x.Value is null || x.Value.Length > 2000))
            throw new DomainException("VAL_INVALID_INPUT", "Dispatch request contains invalid fields.");

        var template = await _db.NotificationTemplates.AsNoTracking().SingleOrDefaultAsync(t =>
            t.Code == request.TemplateCode.Trim() && t.Locale == request.Locale.Trim() && t.IsActive && !t.IsDeleted, cancellationToken);
        if (template is null)
            throw new DomainException("TEMPLATE_NOT_FOUND", "Active notification template was not found.", 404);
        var replay = await FindReplayAsync(request, template.Id, cancellationToken);
        if (replay is not null) return replay;

        var channel = await _db.NotificationChannels.AsNoTracking().SingleOrDefaultAsync(c => c.Code == template.ChannelCode && !c.IsDeleted, cancellationToken);
        if (channel is null)
            throw new DomainException("CHANNEL_NOT_FOUND", "Notification channel configuration was not found.", 409);
        if (channel.Code is not (ChannelType.Email or ChannelType.InApp or ChannelType.Push))
            throw new DomainException("CHANNEL_NOT_SUPPORTED", "Notification channel is not supported by this dispatch feature.");
        if (channel.Code == ChannelType.Email &&
            (string.IsNullOrWhiteSpace(request.Destination) || !new EmailAddressAttribute().IsValid(request.Destination) || request.Destination.Length > 500))
            throw new DomainException("VAL_INVALID_INPUT", "A valid email destination is required for this template.");
        if (channel.Code == ChannelType.Push && !string.IsNullOrWhiteSpace(request.Destination))
            throw new DomainException("VAL_INVALID_INPUT", "Push destination must be omitted; devices are selected by user ID.");

        var mandatory = template.IsMandatory || Notification.Application.Services.Notifications.NotificationPreferencePolicy.IsCritical(template.NotificationType);
        if (!mandatory) mandatory = (await Notification.Application.Services.Notifications.NotificationPreferencePolicy.MandatoryPairsAsync(_db, cancellationToken)).Contains((template.NotificationType, channel.Code));
        var disabledByPreference = !mandatory && await _db.NotificationPreferences.AsNoTracking().AnyAsync(p =>
            p.UserId == request.UserId && p.NotificationType == template.NotificationType && p.ChannelCode == channel.Code && p.IsDisabled && !p.IsDeleted, cancellationToken);
        var suppressed = channel.IsDisabled || disabledByPreference;
        var devices = new List<DeviceToken>();
        if (channel.Code == ChannelType.Push && !suppressed)
        {
            devices = await _db.DeviceTokens.AsNoTracking().Where(d => d.UserId == request.UserId && d.IsActive && !d.IsDeleted)
                .OrderBy(d => d.Id).Take(101).ToListAsync(cancellationToken);
            if (devices.Count > 100)
                throw new DomainException("TOO_MANY_DEVICES", "Push dispatch supports at most 100 active devices per user.", 409);
        }
        var noDevices = channel.Code == ChannelType.Push && !suppressed && devices.Count == 0;
        var skipped = suppressed || noDevices;
        var title = TemplateRenderer.Render(template.SubjectTemplate, request.Data, false, 500);
        var body = TemplateRenderer.Render(template.BodyTemplate, request.Data, channel.Code == ChannelType.Email, 4000);
        var now = DateTimeOffset.UtcNow;
        var immediate = channel.Code == ChannelType.InApp && !skipped;
        var notification = new NotificationRecord
        {
            Id = Guid.NewGuid(), UserId = request.UserId!.Value, SourceEventId = request.SourceEventId,
            TemplateId = template.Id, NotificationType = template.NotificationType, ChannelScope = ChannelScope.Specific,
            Title = title, Body = body, IsRead = skipped, ReadAt = skipped ? now : null,
            IsDeleted = skipped, DeletedAt = skipped ? now : null,
            Status = immediate || skipped ? NotificationStatus.Completed : NotificationStatus.Pending,
            CreatedAt = now, UpdatedAt = now
        };
        var destinations = channel.Code == ChannelType.Push && !skipped ? devices.Select(d => d.Id.ToString()).ToArray() :
            new[] { channel.Code == ChannelType.Email ? request.Destination!.Trim() : request.UserId.Value.ToString() };
        var deliveries = destinations.Select(destination => new NotificationDelivery
        {
            Id = Guid.NewGuid(), NotificationId = notification.Id, ChannelCode = channel.Code, Destination = destination,
            Status = skipped ? DeliveryStatus.Skipped : immediate ? DeliveryStatus.Sent : DeliveryStatus.Pending,
            ErrorCode = noDevices ? "PUSH_NO_ACTIVE_DEVICES" : null,
            AttemptCount = immediate ? 1 : 0, LastAttemptAt = immediate ? now : null,
            RowVersion = 1, CreatedAt = now, UpdatedAt = now
        }).ToList();
        var logs = deliveries.Select(delivery => new NotificationLog
        {
            Id = Guid.NewGuid(), NotificationId = notification.Id, DeliveryId = delivery.Id,
            Action = skipped ? LogAction.Suppressed : immediate ? LogAction.Sent : LogAction.Created,
            Level = Notification.Domain.Enums.LogLevel.Info,
            Message = channel.IsDisabled ? "Notification channel is disabled." : disabledByPreference ? "User disabled optional notifications." :
                noDevices ? "No active devices are available for push delivery." : "Template dispatch created.",
            Metadata = JsonSerializer.Serialize(new { request.SourceEventId, template.Code, IsMandatory = mandatory }),
            CreatedAt = now, UpdatedAt = now
        }).ToList();
        _db.Notifications.Add(notification);
        _db.NotificationDeliveries.AddRange(deliveries);
        _db.NotificationLogs.AddRange(logs);
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            _db.NotificationLogs.RemoveRange(logs);
            _db.NotificationDeliveries.RemoveRange(deliveries);
            _db.Notifications.Remove(notification);
            var winner = await FindReplayAsync(request, template.Id, cancellationToken);
            if (winner is not null) return winner;
            throw;
        }
        return Response(notification, deliveries, false);
    }

    private async Task<DispatchResponse?> FindReplayAsync(CreateDispatchRequest request, Guid templateId, CancellationToken cancellationToken)
    {
        var notification = await _db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(n =>
            n.SourceEventId == request.SourceEventId && n.UserId == request.UserId && n.TemplateId == templateId, cancellationToken);
        if (notification is null) return null;
        var deliveries = await _db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().Where(d => d.NotificationId == notification.Id).ToListAsync(cancellationToken);
        return Response(notification, deliveries, true);
    }

    private static DispatchResponse Response(NotificationRecord notification, IReadOnlyList<NotificationDelivery> deliveries, bool replay)
    {
        var ordered = deliveries.OrderBy(d => d.Id).ToList();
        if (ordered.Count == 0)
            throw new DomainException("DELIVERY_STATE_INVALID", "Notification has no persisted deliveries.", 409);
        var first = ordered[0];
        return new(notification.Id, first.Id, first.ChannelCode.ToString(), notification.Status.ToString(), first.Status.ToString(), replay)
        {
            Deliveries = ordered.Select(d => new DispatchDeliveryResponse(d.Id, d.Status.ToString())).ToList()
        };
    }
}
