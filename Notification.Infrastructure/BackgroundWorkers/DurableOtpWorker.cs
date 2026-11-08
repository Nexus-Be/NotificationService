namespace Notification.Infrastructure.BackgroundWorkers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notification.Application.Services.Email;
using Notification.Application.Services.Health;

public sealed class DurableOtpWorker(IServiceScopeFactory scopes,OtpWorkerSettings settings,TimeProvider time,
    ILogger<DurableOtpWorker> logger,IWorkerHealthReporter health) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        settings.Validate();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                health.Report(WorkerName.OtpEmail,WorkerActivity.Running);
                try
                {
                    using var scope = scopes.CreateScope();
                    var found = await scope.ServiceProvider.GetRequiredService<IOtpEmailDispatchService>().ProcessNextAsync(stoppingToken);
                    health.Report(WorkerName.OtpEmail,WorkerActivity.Waiting);
                    if (found) continue;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { health.Report(WorkerName.OtpEmail,WorkerActivity.Failed); logger.LogError(ex,"OTP worker database iteration failed; retrying after polling interval."); }
                try { await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds),time,stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
        finally { health.Report(WorkerName.OtpEmail,WorkerActivity.Stopped); }
    }
}
