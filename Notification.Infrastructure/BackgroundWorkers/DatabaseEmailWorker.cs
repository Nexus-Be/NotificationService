namespace Notification.Infrastructure.BackgroundWorkers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notification.Application.Services.Deliveries;
using Notification.Application.Services.Health;

public sealed class DatabaseEmailWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DeliveryWorkerSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<DatabaseEmailWorker> _logger;
    private readonly IWorkerHealthReporter? _health;
    public DatabaseEmailWorker(IServiceScopeFactory scopes, DeliveryWorkerSettings settings, TimeProvider time,
        ILogger<DatabaseEmailWorker> logger, IWorkerHealthReporter? health = null)
    {
        _scopes = scopes; _settings = settings; _settings.Validate(); _time = time; _logger = logger; _health = health;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled) { _health?.Report(WorkerName.Delivery,WorkerActivity.Disabled); return; }
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _health?.Report(WorkerName.Delivery,WorkerActivity.Running);
                try
                {
                    using var scope = _scopes.CreateScope();
                    var found = await scope.ServiceProvider.GetRequiredService<IDeliveryService>().ProcessNextAsync(stoppingToken);
                    _health?.Report(WorkerName.Delivery,WorkerActivity.Waiting);
                    if (found) continue;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _health?.Report(WorkerName.Delivery,WorkerActivity.Failed);
                    _logger.LogError(ex,"Database email worker iteration failed; retrying after the polling interval.");
                }
                try { await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds),_time,stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
        finally { _health?.Report(WorkerName.Delivery,WorkerActivity.Stopped); }
    }
}
