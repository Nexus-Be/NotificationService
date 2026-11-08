namespace Notification.Application.Services.Health;

public sealed class HealthService(IDatabaseHealthProbe database, IWorkerHealthReader workers, TimeProvider time) : IHealthService
{
    public async Task<HealthDetailsResponse> CheckAsync(CancellationToken cancellationToken = default)
    {
        var db = await database.CheckAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshots = workers.Snapshot();
        return new(db.IsHealthy && snapshots.All(s => s.IsHealthy) ? "Healthy" : "Unhealthy",
            "notification-service",time.GetUtcNow().ToUniversalTime(),db,snapshots);
    }
}
