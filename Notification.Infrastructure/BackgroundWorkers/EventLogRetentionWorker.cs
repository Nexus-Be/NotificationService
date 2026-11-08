namespace Notification.Infrastructure.BackgroundWorkers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notification.Application.Services.EventLogs;
using Notification.Application.Services.Health;

public sealed class EventLogRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly EventLogRetentionSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<EventLogRetentionWorker> _logger;
    private readonly IWorkerHealthReporter? _health;
    public EventLogRetentionWorker(IServiceScopeFactory scopes, EventLogRetentionSettings settings,
        TimeProvider time, ILogger<EventLogRetentionWorker> logger, IWorkerHealthReporter? health = null)
    {
        _scopes = scopes; _settings = settings; _settings.Validate(); _time = time; _logger = logger; _health = health;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled) { _health?.Report(WorkerName.EventLogRetention,WorkerActivity.Disabled); return; }
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _health?.Report(WorkerName.EventLogRetention,WorkerActivity.Running);
                try
                {
                    using var scope = _scopes.CreateScope();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.RunTimeoutSeconds),_time);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken,timeout.Token);
                    var result = await scope.ServiceProvider.GetRequiredService<IEventLogRetentionService>().CleanupAsync(linked.Token);
                    _health?.Report(WorkerName.EventLogRetention,WorkerActivity.Waiting);
                    _logger.LogInformation("Event log retention finished: cutoff {Cutoff}, deleted {DeletedCount}, batches {Batches}, batch limit reached {ReachedBatchLimit}.",
                        result.Cutoff,result.DeletedCount,result.BatchesExecuted,result.ReachedBatchLimit);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (OperationCanceledException)
                {
                    _health?.Report(WorkerName.EventLogRetention,WorkerActivity.Failed);
                    _logger.LogWarning("Event log retention run timed out or was cancelled; retrying at the next interval.");
                }
                catch (Exception ex)
                {
                    _health?.Report(WorkerName.EventLogRetention,WorkerActivity.Failed);
                    _logger.LogError(ex,"Event log retention run failed; retrying at the next interval.");
                }
                try { await Task.Delay(TimeSpan.FromSeconds(_settings.IntervalSeconds),_time,stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
        finally { _health?.Report(WorkerName.EventLogRetention,WorkerActivity.Stopped); }
    }
}
