namespace Notification.Application.Interfaces;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Notification.Domain.Entities;

public interface INotificationDbContext
{
    DbSet<NotificationChannel> NotificationChannels { get; }
    DbSet<NotificationTemplate> NotificationTemplates { get; }
    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<DeviceToken> DeviceTokens { get; }
    DbSet<NotificationRecord> Notifications { get; }
    DbSet<NotificationDelivery> NotificationDeliveries { get; }
    DbSet<NotificationLog> NotificationLogs { get; }
    DbSet<EventLog> EventLogs { get; }
    DbSet<BrokerHealthCheck> BrokerHealthChecks { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
