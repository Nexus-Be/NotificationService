namespace Notification.Application.Services.Management;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Notification.Domain.Entities;
using Notification.Domain.Enums;

public sealed partial class NotificationManagementService
{
    public async Task<RetryAcceptanceResponse> RetryAsync(Guid id, RetryDeliveryRequest request, ManagementActor actor, CancellationToken cancellationToken = default)
    {
        ValidId(id); ValidActor(actor);
        if (request.RequestId is null || request.RequestId == Guid.Empty || request.ExpectedRowVersion is null or <= 0 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            throw Error("VAL_INVALID_INPUT", "Retry request ID, version and reason are required.");
        var replay = await RetryReplayAsync(id, request, actor, cancellationToken);
        if (replay is not null) return replay;
        var observed = await _db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken);
        if (observed is null) throw Error("DELIVERY_NOT_FOUND", "Notification delivery was not found.", 404);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var parents = await _db.Notifications.FromSqlInterpolated($"SELECT * FROM \"Notifications\" WHERE \"Id\" = {observed.NotificationId} FOR UPDATE")
            .IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);
        replay = await RetryReplayAsync(id, request, actor, cancellationToken);
        if (replay is not null) return replay;
        var parent = parents.SingleOrDefault();
        var delivery = await _db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && !d.IsDeleted, cancellationToken);
        if (delivery is null) throw Error("DELIVERY_NOT_FOUND", "Notification delivery was not found.", 404);
        if (delivery.RowVersion != request.ExpectedRowVersion.Value)
            throw Error("DELIVERY_VERSION_CONFLICT", "Delivery state changed; reload before retrying.", 409);
        if (parent is null || parent.IsDeleted || parent.TemplateId is null || delivery.Status != DeliveryStatus.Failed ||
            delivery.ChannelCode is not (ChannelType.Email or ChannelType.Push))
            throw Error("DELIVERY_NOT_RETRYABLE", "Only visible failed template-backed email or push deliveries can be retried.", 409);
        if (!await _db.NotificationChannels.AsNoTracking().AnyAsync(c => c.Code == delivery.ChannelCode && !c.IsDeleted && !c.IsDisabled, cancellationToken))
            throw Error("CHANNEL_DISABLED", "Delivery channel is missing or disabled.", 409);
        if (delivery.ChannelCode == ChannelType.Push)
        {
            if (!Guid.TryParse(delivery.Destination, out var deviceId) || !await _db.DeviceTokens.AsNoTracking().AnyAsync(d =>
                d.Id == deviceId && d.UserId == parent.UserId && d.IsActive && !d.IsDeleted, cancellationToken))
                throw Error("DEVICE_TOKEN_INACTIVE", "Push device is inactive or no longer belongs to the recipient.", 409);
        }

        var now = NextTime(delivery.UpdatedAt); var version = delivery.RowVersion + 1;
        var changed = await _db.NotificationDeliveries.Where(d => d.Id == id && d.RowVersion == delivery.RowVersion && d.Status == DeliveryStatus.Failed)
            .ExecuteUpdateAsync(set => set.SetProperty(d => d.Status, DeliveryStatus.Pending).SetProperty(d => d.AttemptCount, 0)
                .SetProperty(d => d.LastAttemptAt, (DateTimeOffset?)null).SetProperty(d => d.NextRetryAt, now)
                .SetProperty(d => d.ErrorCode, (string?)null).SetProperty(d => d.ErrorMessage, (string?)null)
                .SetProperty(d => d.RowVersion, version).SetProperty(d => d.UpdatedAt, now), cancellationToken);
        if (changed != 1) throw Error("DELIVERY_VERSION_CONFLICT", "Delivery state changed; reload before retrying.", 409);
        await _db.Notifications.Where(n => n.Id == parent.Id).ExecuteUpdateAsync(set => set
            .SetProperty(n => n.Status, NotificationStatus.Pending).SetProperty(n => n.UpdatedAt, now), cancellationToken);
        var record = new DeliveryRetryRequest
        {
            Id = request.RequestId.Value, DeliveryId = id, ActorId = actor.UserId, Reason = request.Reason.Trim(),
            ExpectedRowVersion = request.ExpectedRowVersion.Value, AcceptedRowVersion = version,
            PreviousAttemptCount = delivery.AttemptCount, CreatedAt = now, UpdatedAt = now
        };
        var log = new NotificationLog
        {
            Id = Guid.NewGuid(), NotificationId = parent.Id, DeliveryId = id, Action = LogAction.Retry,
            Level = Notification.Domain.Enums.LogLevel.Info, Message = "Manual retry accepted.",
            Metadata = JsonSerializer.Serialize(new { ManualRetry = true, RequestId = record.Id, ActorId = actor.UserId, PreviousAttemptCount = delivery.AttemptCount }),
            CreatedAt = now, UpdatedAt = now
        };
        _db.DeliveryRetryRequests.Add(record); _db.NotificationLogs.Add(log);
        var audit = Audit(AuditAction.DeliveryRetry, nameof(NotificationDelivery), id,
            new { Status = delivery.Status.ToString(), delivery.AttemptCount, delivery.RowVersion },
            new { Status = "Pending", AttemptCount = 0, RowVersion = version, RequestId = record.Id, record.Reason }, actor);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (UniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            _db.DeliveryRetryRequests.Remove(record); _db.NotificationLogs.Remove(log); _db.AuditLogs.Remove(audit);
            var winner = await RetryReplayAsync(id, request, actor, cancellationToken);
            if (winner is not null) return winner;
            throw;
        }
        return new(record.Id,id,record.AcceptedRowVersion,record.CreatedAt,false);
    }

    private async Task<RetryAcceptanceResponse?> RetryReplayAsync(Guid id, RetryDeliveryRequest request, ManagementActor actor, CancellationToken cancellationToken)
    {
        var existing = await _db.DeliveryRetryRequests.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(r => r.Id == request.RequestId, cancellationToken);
        if (existing is null) return null;
        if (existing.DeliveryId != id || existing.ActorId != actor.UserId || existing.ExpectedRowVersion != request.ExpectedRowVersion || existing.Reason != request.Reason.Trim())
            throw Error("RETRY_REQUEST_CONFLICT", "Retry request ID was already used for different parameters or actor.", 409);
        return new(existing.Id,id,existing.AcceptedRowVersion,existing.CreatedAt,true);
    }
}
