namespace Notification.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Notification.Domain.Entities;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<EventLog> EventLogs => Set<EventLog>();
    public DbSet<BrokerHealthCheck> BrokerHealthChecks => Set<BrokerHealthCheck>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

public class NotificationDbContext : AppDbContext
{
    public NotificationDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
}
