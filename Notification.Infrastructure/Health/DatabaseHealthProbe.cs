namespace Notification.Infrastructure.Health;

using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Application.Services.Health;

public sealed class DatabaseHealthProbe(INotificationDbContext db, HealthSettings settings, TimeProvider time) : IDatabaseHealthProbe
{
    public async Task<DatabaseHealthResponse> CheckAsync(CancellationToken cancellationToken = default)
    {
        settings.Validate();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.DatabaseTimeoutSeconds),time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,timeout.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!await db.Database.CanConnectAsync(linked.Token))
            {
                linked.Token.ThrowIfCancellationRequested();
                return new("Unhealthy","DATABASE_UNAVAILABLE");
            }
            if ((await db.Database.GetPendingMigrationsAsync(linked.Token)).Any())
                return new("Unhealthy","DATABASE_MIGRATIONS_PENDING");
            await db.EventLogs.IgnoreQueryFilters().AsNoTracking().Select(e => new { e.Id,
                HasHash = e.RequestHash != null, HasHeaders = e.Headers != null, HasTrace = e.TraceId != null }).Take(1).ToListAsync(linked.Token);
            await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
                .Select(d => new { d.Id,d.RowVersion,d.Status,d.AttemptCount }).Take(1).ToListAsync(linked.Token);
            await db.OtpEmailDispatches.IgnoreQueryFilters().AsNoTracking().Select(j => new { j.Id,j.Status,j.RowVersion,HasPayload = j.ProtectedPayload != null }).Take(1).ToListAsync(linked.Token);
            return new("Healthy",null);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new("Unhealthy",timeout.IsCancellationRequested ? "DATABASE_TIMEOUT" : "DATABASE_SCHEMA_UNAVAILABLE");
        }
    }
}
