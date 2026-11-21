namespace Notification.Application.Services.ServiceEvents;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Common.Extensions;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

public sealed record ServiceEventResponse(Guid NotificationId, string SourceEventId, bool IsReplay);

internal static class InAppEventWriter
{
    public static async Task<ServiceEventResponse> CreateAsync(INotificationDbContext db, TimeProvider clock,
        string source, string eventId, Guid user, NotificationType type, string title, string body,
        object details, string? trace, CancellationToken ct)
    {
        var key = $"{source}:{eventId}";
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
        var snapshot = JsonSerializer.Serialize(new { user, type, details });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8031))", ct);
        var existing = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.SourceEventId == id).ToListAsync(ct);
        if (existing.Count > 0)
        {
            var row = existing.Count == 1 ? existing[0] : null;
            if (row?.Payload is null || row.UserId != user || row.NotificationType != type) throw Conflict();
            using var payload = JsonDocument.Parse(row.Payload);
            if (!payload.RootElement.TryGetProperty("requestHash", out var saved) || saved.GetString() != hash)
                throw Conflict();
            return new(row.Id, eventId, true);
        }

        var channel = await db.NotificationChannels.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == ChannelType.InApp && !c.IsDeleted, ct)
            ?? throw new DomainException("CHANNEL_NOT_FOUND", "In-app channel was not found.", 409);
        var disabled = await db.NotificationPreferences.AsNoTracking().AnyAsync(p => p.UserId == user &&
            p.NotificationType == type && p.ChannelCode == ChannelType.InApp && p.IsDisabled && !p.IsDeleted, ct);
        var visible = !channel.IsDisabled && !disabled;
        var now = clock.GetUtcNow();
        var record = new NotificationRecord
        {
            Id = Guid.NewGuid(), UserId = user, SourceEventId = id, NotificationType = type,
            ChannelScope = ChannelScope.Specific, Title = title, Body = body,
            Status = NotificationStatus.Completed, IsInboxVisible = visible, IsRead = !visible,
            Payload = JsonSerializer.Serialize(new { type = type.ToString(), sourceService = source,
                sourceEventId = eventId, requestHash = hash, details, traceId = trace }),
            CreatedAt = now, UpdatedAt = now
        };
        db.Notifications.Add(record);
        db.NotificationDeliveries.Add(new NotificationDelivery
        {
            Id = Guid.NewGuid(), NotificationId = record.Id, ChannelCode = ChannelType.InApp,
            Destination = user.ToString(), Status = visible ? DeliveryStatus.Sent : DeliveryStatus.Skipped,
            AttemptCount = 1, LastAttemptAt = now, RowVersion = 1, CreatedAt = now, UpdatedAt = now
        });
        db.AddAuditLog(AuditAction.ServiceEventAccepted, nameof(NotificationRecord), record.Id.ToString(),
            newValues: new { Source = source, SourceEventId = eventId, Type = type.ToString(), Visible = visible }, traceId: trace);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(record.Id, eventId, false);
    }

    private static DomainException Conflict() =>
        new("SERVICE_EVENT_CONFLICT", "Source event has different notification data.", 409);
}
