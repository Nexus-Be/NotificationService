namespace Notification.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>Generates migrations without booting the API or using its runtime secrets.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("NOTIFICATION_DESIGNTIME_CONNECTION")
            ?? "Host=127.0.0.1;Port=1;Database=notification_design_time;Username=unused;Password=unused";
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
    }
}
