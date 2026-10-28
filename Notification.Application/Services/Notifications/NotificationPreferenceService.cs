
using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;

namespace Notification.Application.Services.Notifications;

public sealed class NotificationPreferenceService
    : INotificationPreferenceService
{
    private readonly INotificationDbContext _db;

    public NotificationPreferenceService(
        INotificationDbContext db)
    {
        _db = db;
    }

    // NOTI-03: Get notification preferences
    public async Task<NotificationPreferencePageDto> GetAsync(
        Guid userId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        if (pageIndex < 1 || pageSize < 1 || pageSize > 100)
            throw new ArgumentException(
                "Invalid pagination parameters.");

        var available = await GetAvailablePreferencesAsync(
            cancellationToken);

        var preferences = await _db.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId && !p.IsDeleted)
            .ToListAsync(cancellationToken);

        var saved = preferences
            .GroupBy(p => (p.NotificationType, p.ChannelCode))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(p => p.UpdatedAt)
                      .First().IsDisabled);

        var allItems = available
            .OrderBy(p => p.Key.Item1)
            .ThenBy(p => p.Key.Item2)
            .Select(p =>
            {
                var isDisabled = saved.TryGetValue(
                    p.Key, out var disabled) && disabled;

                return new NotificationPreferenceDto(
                    p.Key.Item1,
                    p.Key.Item2,
                    p.Value || !isDisabled,
                    p.Value);
            })
            .ToList();

        var total = allItems.Count;

        var offset = ((long)pageIndex - 1) * pageSize;

        if (offset > int.MaxValue)
            throw new ArgumentException(
                "Requested page exceeds the supported range.");

        var items = allItems
            .Skip((int)offset)
            .Take(pageSize)
            .ToList();

        return new NotificationPreferencePageDto(
            items,
            total,
            pageIndex,
            pageSize);
    }

    // NOTI-04: Update notification preferences
    public async Task<NotificationPreferenceUpdateResult> UpdateAsync(
        Guid userId,
        IReadOnlyCollection<UpdatePreferenceDto> preferences,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        ArgumentNullException.ThrowIfNull(preferences);

        if (preferences.Count == 0 || preferences.Count > 100)
            throw new ArgumentException(
                "Preferences list is invalid.");

        var available = await GetAvailablePreferencesAsync(
            cancellationToken);

        var keys = new HashSet<(NotificationType, ChannelType)>();

        // Validate the entire request before changing data
        foreach (var item in preferences)
        {
            var key = (item.NotificationType, item.ChannelCode);

            if (!Enum.IsDefined(item.NotificationType)
                || !Enum.IsDefined(item.ChannelCode)
                || !available.ContainsKey(key)
                || !keys.Add(key))
            {
                throw new ArgumentException(
                    "Notification type or channel is invalid.");
            }

            if (available[key] && !item.IsEnabled)
            {
                throw new InvalidOperationException(
                    "MANDATORY_NOTIFICATION_CANNOT_DISABLE");
            }
        }

        var existing = await _db.NotificationPreferences
            .AsTracking()
            .Where(p => p.UserId == userId && !p.IsDeleted)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var updatedCount = 0;
        var results = new List<NotificationPreferenceDto>();

        foreach (var item in preferences)
        {
            var preference = existing.FirstOrDefault(p =>
                p.NotificationType == item.NotificationType &&
                p.ChannelCode == item.ChannelCode);

            if (preference is null)
            {
                preference = new NotificationPreference
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    NotificationType = item.NotificationType,
                    ChannelCode = item.ChannelCode,
                    IsDisabled = !item.IsEnabled,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _db.NotificationPreferences.Add(preference);
                existing.Add(preference);
                updatedCount++;
            }
            else if (preference.IsDisabled == item.IsEnabled)
            {
                preference.IsDisabled = !item.IsEnabled;
                preference.UpdatedAt = now;
                updatedCount++;
            }

            results.Add(new NotificationPreferenceDto(
                item.NotificationType,
                item.ChannelCode,
                item.IsEnabled,
                available[(
                    item.NotificationType,
                    item.ChannelCode)]));
        }

        if (updatedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new NotificationPreferenceUpdateResult(
            updatedCount,
            results);
    }

    // Active template + enabled channel define available options
    private async Task<Dictionary<
        (NotificationType, ChannelType), bool>>
        GetAvailablePreferencesAsync(
            CancellationToken cancellationToken)
    {
        var templates = await _db.NotificationTemplates
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.IsActive)
            .Select(t => new
            {
                t.NotificationType,
                t.ChannelCode,
                t.IsMandatory
            })
            .ToListAsync(cancellationToken);

        var channels = await _db.NotificationChannels
            .AsNoTracking()
            .Where(c => !c.IsDeleted && !c.IsDisabled)
            .Select(c => c.Code)
            .ToListAsync(cancellationToken);

        var enabledChannels = channels.ToHashSet();

        return templates
            .Where(t => enabledChannels.Contains(t.ChannelCode))
            .GroupBy(t => (t.NotificationType, t.ChannelCode))
            .ToDictionary(
                g => g.Key,
                g => g.Any(t => t.IsMandatory));
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException(
                "User ID must not be empty.",
                nameof(userId));
    }
}
