namespace Notification.Tests;

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Application.Services.Email;
using Notification.Application.Services.Health;
using Notification.Infrastructure.BackgroundWorkers;
using Xunit;

public sealed class DurableOtpWorkerHostTests
{
    [Fact]
    public async Task IterationFailure_ReportsFailureThenRecoversInFreshScope()
    {
        var states = new ConcurrentQueue<WorkerActivity>();
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var health = new Mock<IWorkerHealthReporter>();
        health.Setup(h => h.Report(WorkerName.OtpEmail,It.IsAny<WorkerActivity>())).Callback<WorkerName,WorkerActivity>((_,a) =>
        { states.Enqueue(a); if (a == WorkerActivity.Waiting) recovered.TrySetResult(); });
        var resolutions = 0;
        using var provider = new ServiceCollection().AddScoped(_ =>
        {
            var service = new Mock<IOtpEmailDispatchService>();
            if (Interlocked.Increment(ref resolutions) == 1)
                service.Setup(s => s.ProcessNextAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Test database error."));
            else service.Setup(s => s.ProcessNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            return service.Object;
        }).BuildServiceProvider();
        using var worker = new DurableOtpWorker(provider.GetRequiredService<IServiceScopeFactory>(),new(),TimeProvider.System,NullLogger<DurableOtpWorker>.Instance,health.Object);
        await worker.StartAsync(CancellationToken.None);
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await worker.StopAsync(stop.Token);
        Assert.Equal(2,resolutions);
        Assert.Equal(new[] { WorkerActivity.Running,WorkerActivity.Failed,WorkerActivity.Running,WorkerActivity.Waiting,WorkerActivity.Stopped },states.ToArray());
    }

    [Fact]
    public async Task HostStop_CancelsActiveIterationAndReportsStopped()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Mock<IOtpEmailDispatchService>(); var health = new Mock<IWorkerHealthReporter>();
        service.Setup(s => s.ProcessNextAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken ct) =>
        { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan,ct); return false; });
        using var provider = new ServiceCollection().AddScoped(_ => service.Object).BuildServiceProvider();
        using var worker = new DurableOtpWorker(provider.GetRequiredService<IServiceScopeFactory>(),new(),TimeProvider.System,NullLogger<DurableOtpWorker>.Instance,health.Object);
        await worker.StartAsync(CancellationToken.None); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await worker.StopAsync(stop.Token);
        health.Verify(h => h.Report(WorkerName.OtpEmail,WorkerActivity.Stopped),Times.Once);
        health.Verify(h => h.Report(WorkerName.OtpEmail,WorkerActivity.Failed),Times.Never);
    }
}
