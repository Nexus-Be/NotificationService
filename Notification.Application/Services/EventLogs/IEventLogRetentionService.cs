namespace Notification.Application.Services.EventLogs;

public sealed record EventLogRetentionResult(DateTimeOffset Cutoff, int DeletedCount, int BatchesExecuted, bool ReachedBatchLimit);

public interface IEventLogRetentionService
{
    Task<EventLogRetentionResult> CleanupAsync(CancellationToken cancellationToken = default);
}
