namespace Notification.Application.Services.Deliveries;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Application.Common.Extensions;
using Notification.Domain.Entities;
using Notification.Domain.Enums;

public sealed class DeliveryService : IDeliveryService
{
    private readonly INotificationDbContext _db;
    private readonly IEmailDeliverySender _sender;
    private readonly DeliveryWorkerSettings _settings;
    private readonly TimeProvider _time;
    private readonly IPushDeliverySender? _pushSender;

    public DeliveryService(INotificationDbContext db, IEmailDeliverySender sender, DeliveryWorkerSettings settings, TimeProvider time, IPushDeliverySender? pushSender = null)
    {
        _db = db;
        _sender = sender;
        _settings = settings;
        _settings.Validate();
        _time = time;
        _pushSender = pushSender;
    }

    private IQueryable<NotificationDelivery> Eligible(DateTimeOffset now)
    {
        var expired = now.AddSeconds(-_settings.LeaseSeconds);
        return _db.NotificationDeliveries.Where(d => (d.ChannelCode == ChannelType.Email || d.ChannelCode == ChannelType.Push) && !d.IsDeleted &&
            d.Notification != null && !d.Notification.IsDeleted && d.Notification.TemplateId != null &&
            d.Channel != null && !d.Channel.IsDeleted && !d.Channel.IsDisabled &&
            ((d.Status == DeliveryStatus.Pending && (!d.NextRetryAt.HasValue || d.NextRetryAt <= now)) ||
             (d.Status == DeliveryStatus.Sending && (!d.LastAttemptAt.HasValue || d.LastAttemptAt <= expired))));
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled) return false;
        var now = _time.GetUtcNow();
        var candidate = await Eligible(now).AsNoTracking().Include(d => d.Notification)
            .OrderBy(d => d.NextRetryAt ?? d.CreatedAt).ThenBy(d => d.Id).FirstOrDefaultAsync(cancellationToken);
        if (candidate is null) return false;

        now = _time.GetUtcNow();
        var exhausted = candidate.AttemptCount >= _settings.MaxAttempts;
        var attempt = exhausted ? candidate.AttemptCount : candidate.AttemptCount + 1;
        var version = candidate.RowVersion + 1;
        await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            // Claim and completion both lock parent before delivery to avoid FK/row-lock inversion.
            await _db.Notifications.FromSqlInterpolated(
                $"SELECT * FROM \"Notifications\" WHERE \"Id\" = {candidate.NotificationId} FOR UPDATE")
                .IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);
            now = _time.GetUtcNow();
            var claimed = await Eligible(now).Where(d => d.Id == candidate.Id && d.RowVersion == candidate.RowVersion)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(d => d.Status, DeliveryStatus.Sending)
                    .SetProperty(d => d.RowVersion, version)
                    .SetProperty(d => d.AttemptCount, attempt)
                    .SetProperty(d => d.LastAttemptAt, now)
                    .SetProperty(d => d.NextRetryAt, (DateTimeOffset?)null)
                    .SetProperty(d => d.UpdatedAt, now), cancellationToken);
            if (claimed == 0) return true;
            AddLog(candidate, LogAction.Dispatching, "Notification delivery claimed.", now, attempt,
                new { RowVersion = version, Recovered = candidate.Status == DeliveryStatus.Sending });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        DeliveryOutcome result;
        DeviceToken? deviceSnapshot = null;
        if (exhausted)
        {
            result = DeliveryOutcome.Failure("RETRY_LIMIT_EXCEEDED", "Notification delivery exhausted its attempt limit.");
        }
        else
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.AttemptTimeoutSeconds), _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                if (candidate.ChannelCode == ChannelType.Push)
                {
                    if (!Guid.TryParse(candidate.Destination, out var deviceId))
                        result = DeliveryOutcome.Skip("DEVICE_TOKEN_INACTIVE", "Push device reference is invalid.");
                    else
                    {
                        deviceSnapshot = await _db.DeviceTokens.AsNoTracking().SingleOrDefaultAsync(d => d.Id == deviceId &&
                            d.UserId == candidate.Notification!.UserId && d.IsActive && !d.IsDeleted, linked.Token);
                        if (deviceSnapshot is null)
                            result = DeliveryOutcome.Skip("DEVICE_TOKEN_INACTIVE", "Push device was revoked, deleted or reassigned.");
                        else if (_pushSender is null)
                            result = DeliveryOutcome.Failure("FCM_NOT_CONFIGURED", "Push provider is not configured.");
                        else
                        {
                            var push = new PushDeliveryMessage(candidate.Id, candidate.NotificationId, candidate.Notification!.UserId,
                                deviceSnapshot.Token, candidate.Notification.Title ?? string.Empty, candidate.Notification.Body ?? string.Empty);
                            var sent = await _pushSender.SendAsync(push, linked.Token).WaitAsync(linked.Token);
                            result = new(sent.Succeeded, sent.IsTransient, sent.ErrorCode, sent.ErrorMessage,
                                InvalidateToken: sent.InvalidateToken, RetryAfter: sent.RetryAfter, ProviderMessageId: sent.ProviderMessageId);
                        }
                    }
                }
                else
                {
                    var message = new EmailDeliveryMessage(candidate.Id, candidate.Destination,
                        candidate.SubjectOverride ?? candidate.Notification!.Title ?? string.Empty, candidate.BodyOverride ?? candidate.Notification!.Body ?? string.Empty);
                    var sent = await _sender.SendAsync(message, linked.Token).WaitAsync(linked.Token);
                    result = new(sent.Succeeded, sent.IsTransient, sent.ErrorCode, sent.ErrorMessage);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Leave the durable Sending claim for recovery after its lease expires.
                throw;
            }
            catch (OperationCanceledException)
            {
                result = DeliveryOutcome.Failure(candidate.ChannelCode == ChannelType.Push ? "PUSH_TIMEOUT" : "EMAIL_TIMEOUT", "Notification provider request timed out.", true);
            }
            catch (Exception)
            {
                result = DeliveryOutcome.Failure(candidate.ChannelCode == ChannelType.Push ? "PUSH_PROVIDER_ERROR" : "EMAIL_PROVIDER_ERROR", "Notification provider request failed unexpectedly.", true);
            }
        }

        await CompleteAsync(candidate, version, attempt, result, deviceSnapshot, cancellationToken);
        return true;
    }

    private async Task CompleteAsync(NotificationDelivery claim, long version, int attempt, DeliveryOutcome result, DeviceToken? deviceSnapshot, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        // Serialize parent-state aggregation when multiple deliveries belong to one notification.
        var parents = await _db.Notifications.FromSqlInterpolated(
            $"SELECT * FROM \"Notifications\" WHERE \"Id\" = {claim.NotificationId} FOR UPDATE")
            .IgnoreQueryFilters().AsTracking().ToListAsync(cancellationToken);
        var parent = parents.Single();
        var delivery = await _db.NotificationDeliveries.AsTracking().SingleAsync(d => d.Id == claim.Id, cancellationToken);
        if (delivery.Status != DeliveryStatus.Sending || delivery.RowVersion != version)
            return; // A newer owner reclaimed this delivery. Never overwrite its result.

        var now = _time.GetUtcNow();
        var retry = !result.Succeeded && !result.Skipped && result.IsTransient && attempt < _settings.MaxAttempts;
        delivery.Status = result.Skipped ? DeliveryStatus.Skipped : result.Succeeded ? DeliveryStatus.Sent : retry ? DeliveryStatus.Pending : DeliveryStatus.Failed;
        var baseDelay = claim.ChannelCode == ChannelType.Push && result.ErrorCode == "FCM_QUOTA_EXCEEDED" ?
            Math.Max(60, _settings.BaseDelaySeconds) : _settings.BaseDelaySeconds;
        var delay = TimeSpan.FromSeconds(baseDelay * Math.Pow(2, attempt - 1));
        if (result.RetryAfter is { } after && after > delay) delay = after;
        delivery.NextRetryAt = retry ? now.Add(delay) : null;
        if (result.InvalidateToken && deviceSnapshot is not null)
        {
            var invalidated = await _db.DeviceTokens.Where(d => d.Id == deviceSnapshot.Id && d.UserId == parent.UserId &&
                d.Token == deviceSnapshot.Token && d.UpdatedAt == deviceSnapshot.UpdatedAt && d.IsActive && !d.IsDeleted)
                .ExecuteUpdateAsync(set => set.SetProperty(d => d.IsActive, false).SetProperty(d => d.UpdatedAt, now), cancellationToken);
            if (invalidated > 0)
                _db.AddAuditLog(AuditAction.DeviceRevoke, nameof(DeviceToken), deviceSnapshot.Id.ToString(),
                    oldValues: new { IsActive = true }, newValues: new { IsActive = false, Reason = "FCM_UNREGISTERED", OwnerId = parent.UserId });
        }
        delivery.ErrorCode = result.Succeeded ? null : Limit(result.ErrorCode ?? "DELIVERY_FAILED", 100);
        delivery.ErrorMessage = result.Succeeded ? null : Limit(result.ErrorMessage ?? "Notification delivery failed.", 1000);
        delivery.RowVersion++;
        delivery.UpdatedAt = now;
        AddLog(delivery, result.Skipped ? LogAction.Suppressed : result.Succeeded ? LogAction.Sent : retry ? LogAction.Retry : LogAction.Failed,
            result.Succeeded ? "Notification provider accepted the message." : delivery.ErrorMessage!, now, attempt,
            new { delivery.ErrorCode, delivery.NextRetryAt, RowVersion = delivery.RowVersion, ProviderMessageId = result.ProviderMessageId is null ? null : Limit(result.ProviderMessageId, 500) });

        var states = await _db.NotificationDeliveries.AsNoTracking()
            .Where(d => d.NotificationId == parent.Id && d.Id != delivery.Id && !d.IsDeleted)
            .Select(d => d.Status).ToListAsync(cancellationToken);
        states.Add(delivery.Status);
        parent.Status = states.Any(s => s is DeliveryStatus.Pending or DeliveryStatus.Sending) ? NotificationStatus.Pending :
            states.Any(s => s == DeliveryStatus.Failed) ?
                states.Any(s => s == DeliveryStatus.Sent) ? NotificationStatus.Partial : NotificationStatus.Failed : NotificationStatus.Completed;
        if (states.All(s => s == DeliveryStatus.Skipped))
        {
            parent.IsDeleted = true;
            parent.DeletedAt = now;
            parent.IsRead = true;
            parent.ReadAt = now;
        }
        parent.UpdatedAt = now;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            // RowVersion fencing also protects against a reclaim racing this final save.
        }
    }

    private void AddLog(NotificationDelivery delivery, LogAction action, string message, DateTimeOffset now, int attempt, object details)
    {
        _db.NotificationLogs.Add(new NotificationLog
        {
            Id = Guid.NewGuid(), NotificationId = delivery.NotificationId, DeliveryId = delivery.Id,
            Action = action, Level = action == LogAction.Failed ? Notification.Domain.Enums.LogLevel.Error :
                action == LogAction.Retry ? Notification.Domain.Enums.LogLevel.Warn : Notification.Domain.Enums.LogLevel.Info,
            Message = message, Metadata = JsonSerializer.Serialize(new { Attempt = attempt, Details = details }),
            CreatedAt = now, UpdatedAt = now
        });
    }

    private sealed record DeliveryOutcome(bool Succeeded, bool IsTransient, string? ErrorCode, string? ErrorMessage,
        bool Skipped = false, bool InvalidateToken = false, TimeSpan? RetryAfter = null, string? ProviderMessageId = null)
    {
        public static DeliveryOutcome Failure(string code, string message, bool transient = false) => new(false, transient, code, message);
        public static DeliveryOutcome Skip(string code, string message) => new(false, false, code, message, Skipped: true);
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];
}
