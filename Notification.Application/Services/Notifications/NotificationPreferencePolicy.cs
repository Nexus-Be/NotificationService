namespace Notification.Application.Services.Notifications;

using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

public static class NotificationPreferencePolicy
{
    public static bool IsCritical(NotificationType type) => type is NotificationType.OrderPaid or NotificationType.SecurityAlert or NotificationType.Otp;
    public static bool IsSupported(ChannelType channel) => channel is ChannelType.Email or ChannelType.Push or ChannelType.InApp;
    public static T Parse<T>(string? value) where T : struct, Enum
    {
        var name = Enum.GetNames<T>().FirstOrDefault(n => string.Equals(n,value?.Trim(),StringComparison.OrdinalIgnoreCase));
        return name is null ? throw Invalid() : Enum.Parse<T>(name);
    }
    public static DomainException Invalid() => new("VAL_INVALID_INPUT","Notification preference or read state request is invalid.");
    public static async Task<HashSet<(NotificationType Type, ChannelType Channel)>> MandatoryPairsAsync(INotificationDbContext db,CancellationToken ct)
    {
        var pairs = await db.NotificationTemplates.AsNoTracking().Where(t => t.IsActive && !t.IsDeleted && t.IsMandatory)
            .Select(t => new { t.NotificationType,t.ChannelCode }).ToListAsync(ct);
        return pairs.Select(p => (p.NotificationType,p.ChannelCode)).ToHashSet();
    }
}
