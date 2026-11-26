namespace Notification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Notification.Application.Services.Routing;
using Notification.Domain.Entities;
public sealed class NotificationRoutingInterceptor(IConfiguration? configuration = null) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
        InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (data.Context is AppDbContext db)
        {
            var records = db.ChangeTracker.Entries<NotificationRecord>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            var deliveries = db.ChangeTracker.Entries<NotificationDelivery>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            await NotificationChannelPlanner.ApplyAsync(db, records, deliveries, ct, configuration?.GetValue<bool>("NotificationRouting:EnablePush") == true);
        }
        return result;
    }
}
