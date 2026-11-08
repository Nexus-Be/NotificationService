namespace Notification.Application.Services.Notifications;

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Common;
using Notification.Application.Common.Extensions;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

public sealed partial class UserNotificationService
{
    public async Task<IReadOnlyList<NotificationPreferenceResponse>> GetPreferencesAsync(Guid userId,CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        var channels = await _db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted &&
            (c.Code == ChannelType.Email || c.Code == ChannelType.Push || c.Code == ChannelType.InApp)).OrderBy(c => c.Code).ToListAsync(cancellationToken);
        var saved = await _db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == userId && !p.IsDeleted).ToListAsync(cancellationToken);
        var lookup = saved.ToDictionary(p => (p.NotificationType,p.ChannelCode));
        var mandatory = await NotificationPreferencePolicy.MandatoryPairsAsync(_db,cancellationToken);
        var result = new List<NotificationPreferenceResponse>();
        foreach (var type in Enum.GetValues<NotificationType>())
        foreach (var channel in channels)
        {
            lookup.TryGetValue((type,channel.Code),out var row);
            var required = NotificationPreferencePolicy.IsCritical(type) || mandatory.Contains((type,channel.Code));
            result.Add(new(row?.Id,type.ToString(),channel.Code.ToString(),!required && row is { IsDisabled: true },
                required,row is null,channel.IsDisabled));
        }
        return result;
    }

    public async Task<UpdatePreferencesResult> UpdatePreferencesAsync(Guid userId,UpdatePreferencesRequest request,CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        if (request?.Preferences is null || request.Preferences.Count is < 1 or > 100) throw NotificationPreferencePolicy.Invalid();
        var desired = new Dictionary<(NotificationType Type,ChannelType Channel),bool>();
        foreach (var item in request.Preferences)
        {
            if (item is null || item.IsDisabled is null) throw NotificationPreferencePolicy.Invalid();
            var type = NotificationPreferencePolicy.Parse<NotificationType>(item.NotificationType);
            var channel = NotificationPreferencePolicy.Parse<ChannelType>(item.ChannelCode);
            if (!NotificationPreferencePolicy.IsSupported(channel) || !desired.TryAdd((type,channel),item.IsDisabled.Value))
                throw NotificationPreferencePolicy.Invalid();
        }
        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (transaction is not null)
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({userId.ToString()}, 8007))",cancellationToken);
        var channels = await _db.NotificationChannels.AsNoTracking().Where(c => !c.IsDeleted).Select(c => c.Code).ToListAsync(cancellationToken);
        if (desired.Keys.Any(k => !channels.Contains(k.Channel)))
            throw new DomainException("CHANNEL_NOT_FOUND","Notification channel was not found.",404);
        var mandatory = await NotificationPreferencePolicy.MandatoryPairsAsync(_db,cancellationToken);
        if (desired.Any(p => p.Value && (NotificationPreferencePolicy.IsCritical(p.Key.Type) || mandatory.Contains(p.Key))))
            throw new DomainException("PREFERENCE_MANDATORY","Mandatory notifications cannot be disabled.",409);
        var existing = await _db.NotificationPreferences.AsTracking().Where(p => p.UserId == userId && !p.IsDeleted).ToListAsync(cancellationToken);
        var count = 0; var now = _time.GetUtcNow().ToUniversalTime();
        foreach (var (pair,disabled) in desired)
        {
            var row = existing.SingleOrDefault(p => p.NotificationType == pair.Type && p.ChannelCode == pair.Channel);
            if (row is not null && row.IsDisabled == disabled) continue;
            object? before = row is null ? null : new { row.IsDisabled };
            if (row is null)
            {
                row = new NotificationPreference { Id = Guid.NewGuid(),UserId = userId,NotificationType = pair.Type,
                    ChannelCode = pair.Channel,CreatedAt = now,UpdatedAt = now };
                _db.NotificationPreferences.Add(row);
            }
            row.IsDisabled = disabled; row.UpdatedAt = now;
            _db.AddAuditLog(AuditAction.PreferenceUpdate,nameof(NotificationPreference),row.Id.ToString(),
                oldValues:before,newValues:new { NotificationType = pair.Type.ToString(),ChannelCode = pair.Channel.ToString(),IsDisabled = disabled },
                userId:userId.ToString());
            count++;
        }
        try
        {
            if (count > 0) await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
        {
            throw new DomainException("PREFERENCE_CONFLICT","Notification preferences changed concurrently; retry the request.",409);
        }
        return new(count);
    }

    public async Task<NotificationDto> SetReadStateAsync(Guid userId,Guid id,SetNotificationReadStateRequest request,CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        if (id == Guid.Empty || request?.IsRead is null) throw NotificationPreferencePolicy.Invalid();
        var desired = request.IsRead.Value;
        var now = _time.GetUtcNow().ToUniversalTime();
        await _db.Notifications.Where(n => n.Id == id && n.UserId == userId && !n.IsDeleted && n.IsInboxVisible && n.IsRead != desired)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.IsRead,desired)
                .SetProperty(n => n.ReadAt,desired ? now : (DateTimeOffset?)null)
                .SetProperty(n => n.UpdatedAt,n => n.UpdatedAt > now ? n.UpdatedAt : now),cancellationToken);
        var row = await _db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId && !n.IsDeleted && n.IsInboxVisible,cancellationToken);
        if (row is null) throw new DomainException("NOTIFICATION_NOT_FOUND","Notification was not found or does not belong to the user.",404);
        return new(row.Id,row.Title,row.Body,row.IsRead,row.CreatedAt,row.ReadAt);
    }
}
