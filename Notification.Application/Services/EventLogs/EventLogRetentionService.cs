namespace Notification.Application.Services.EventLogs;

using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;

public sealed class EventLogRetentionService : IEventLogRetentionService
{
    private readonly INotificationDbContext _db;
    private readonly EventLogRetentionSettings _settings;
    private readonly TimeProvider _time;
    public EventLogRetentionService(INotificationDbContext db, EventLogRetentionSettings settings, TimeProvider time)
    {
        _db = db; _settings = settings; _settings.Validate(); _time = time;
    }
    public async Task<EventLogRetentionResult> CleanupAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = _time.GetUtcNow().ToUniversalTime().AddDays(-EventLogRetentionSettings.RetentionDays);
        cutoff = new DateTimeOffset(cutoff.Ticks / 10 * 10, TimeSpan.Zero);
        if (!_settings.Enabled) return new(cutoff, 0, 0, false);
        var deleted = 0;
        for (var batch = 1; batch <= _settings.MaxBatchesPerRun; batch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Atomic PostgreSQL batch; include soft-hidden rows. Producer time does not control expiration.
            var affected = await _db.Database.ExecuteSqlInterpolatedAsync($"""
                WITH expired AS (
                    SELECT "Id" FROM "EventLogs"
                    WHERE "CreatedAt" < {cutoff}
                    ORDER BY "CreatedAt", "Id"
                    LIMIT {_settings.BatchSize}
                    FOR UPDATE SKIP LOCKED
                )
                DELETE FROM "EventLogs" AS events USING expired
                WHERE events."Id" = expired."Id"
                """, cancellationToken);
            deleted += affected;
            if (affected < _settings.BatchSize) return new(cutoff, deleted, batch, false);
        }
        return new(cutoff, deleted, _settings.MaxBatchesPerRun, true);
    }
}
