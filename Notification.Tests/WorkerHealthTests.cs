namespace Notification.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Application.Interfaces;
using Notification.Application.Services.Deliveries;
using Notification.Application.Services.Email;
using Notification.Application.Services.EventLogs;
using Notification.Application.Services.Health;
using Notification.Infrastructure;
using Notification.Infrastructure.BackgroundWorkers;
using Notification.Infrastructure.Health;
using Notification.Infrastructure.Services.Email;
using Xunit;

public sealed class WorkerHealthTests
{
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-11-08T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
    private static WorkerHealthResponse Worker(WorkerHealthRegistry registry,WorkerName name)
        => registry.Snapshot().Single(w => w.Name == name.ToString());
    private static WorkerHealthRegistry Registry(TimeProvider clock,bool retention = true,bool delivery = true)
        => new(new() { Enabled = delivery },new() { Enabled = retention },new(),clock);

    [Fact]
    public void InitialAndDisabledStates_AreFailClosedForRequiredWorkers()
    {
        var registry = Registry(new Clock(),retention:false);
        Assert.Equal("NotStarted",Worker(registry,WorkerName.Delivery).Status);
        Assert.False(Worker(registry,WorkerName.OtpEmail).IsHealthy);
        Assert.Equal("Disabled",Worker(registry,WorkerName.EventLogRetention).Status);
        Assert.True(Worker(registry,WorkerName.EventLogRetention).IsHealthy);
        registry.Report(WorkerName.EventLogRetention,WorkerActivity.Stopped);
        Assert.Equal("Disabled",Worker(registry,WorkerName.EventLogRetention).Status);
    }

    [Theory]
    [InlineData(WorkerName.Delivery,150)]
    [InlineData(WorkerName.OtpEmail,120)]
    [InlineData(WorkerName.EventLogRetention,150)]
    public void Running_StallsStrictlyAfterItsTimeBudget(WorkerName name,int seconds)
    {
        var clock = new Clock(); var registry = Registry(clock);
        registry.Report(name,WorkerActivity.Running); clock.Advance(TimeSpan.FromSeconds(seconds));
        Assert.Equal("Running",Worker(registry,name).Status);
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal("Stalled",Worker(registry,name).Status); Assert.False(Worker(registry,name).IsHealthy);
    }

    [Fact]
    public void Waiting_RespectsSchedule_WhileIdleOtpQueueHasNoExpiry()
    {
        var clock = new Clock(); var registry = Registry(clock);
        foreach (var name in Enum.GetValues<WorkerName>()) registry.Report(name,WorkerActivity.Waiting);
        clock.Advance(TimeSpan.FromSeconds(35));
        Assert.Equal("Waiting",Worker(registry,WorkerName.Delivery).Status);
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal("Stalled",Worker(registry,WorkerName.Delivery).Status);
        clock.Advance(TimeSpan.FromDays(10));
        Assert.Equal("Waiting",Worker(registry,WorkerName.OtpEmail).Status);
        Assert.Equal("Stalled",Worker(registry,WorkerName.EventLogRetention).Status);
    }

    [Fact]
    public void Failures_RemainRecoveringUntilSuccess_AndStopBlocksReadiness()
    {
        var clock = new Clock(); var registry = Registry(clock);
        registry.Report(WorkerName.Delivery,WorkerActivity.Failed);
        registry.Report(WorkerName.Delivery,WorkerActivity.Failed);
        Assert.Equal(2,Worker(registry,WorkerName.Delivery).ConsecutiveFailures);
        registry.Report(WorkerName.Delivery,WorkerActivity.Running);
        Assert.Equal("Recovering",Worker(registry,WorkerName.Delivery).Status);
        Assert.False(Worker(registry,WorkerName.Delivery).IsHealthy);
        registry.Report(WorkerName.Delivery,WorkerActivity.Waiting);
        var healthy = Worker(registry,WorkerName.Delivery);
        Assert.Equal(0,healthy.ConsecutiveFailures); Assert.NotNull(healthy.LastSuccessAt); Assert.NotNull(healthy.LastFailureAt);
        registry.Report(WorkerName.Delivery,WorkerActivity.Stopped);
        Assert.False(Worker(registry,WorkerName.Delivery).IsHealthy);
    }

    [Fact]
    public async Task ConcurrentReports_DoNotLoseFailureUpdates()
    {
        var registry = Registry(TimeProvider.System);
        await Task.WhenAll(Enumerable.Range(0,500).Select(_ => Task.Run(() => registry.Report(WorkerName.Delivery,WorkerActivity.Failed))));
        Assert.Equal(500,Worker(registry,WorkerName.Delivery).ConsecutiveFailures);
    }

    [Theory]
    [InlineData("DatabaseTimeoutSeconds",0)]
    [InlineData("DatabaseTimeoutSeconds",16)]
    [InlineData("WorkerGraceSeconds",4)]
    [InlineData("WorkerGraceSeconds",301)]
    [InlineData("OtpStallSeconds",9)]
    [InlineData("OtpStallSeconds",1801)]
    public void Infrastructure_RejectsInvalidHealthSettings(string property,int value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused",
            ["Health:" + property] = value.ToString()
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructureServices(configuration));
    }

    [Fact]
    public async Task CallerCancellation_PropagatesWithoutReadingWorkers()
    {
        using var cts = new CancellationTokenSource();
        var database = new Mock<IDatabaseHealthProbe>();
        database.Setup(d => d.CheckAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            cts.Cancel(); return Task.FromResult(new DatabaseHealthResponse("Healthy",null));
        });
        var reader = new Mock<IWorkerHealthReader>(MockBehavior.Strict);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new HealthService(database.Object,reader.Object,TimeProvider.System).CheckAsync(cts.Token));
        reader.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(WorkerName.Delivery)]
    [InlineData(WorkerName.EventLogRetention)]
    [InlineData(WorkerName.OtpEmail)]
    public async Task ActualWorker_ReportsProcessingWaitingAndShutdown(WorkerName name)
    {
        var registry = Registry(TimeProvider.System);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reporter = new Mock<IWorkerHealthReporter>();
        reporter.Setup(r => r.Report(It.IsAny<WorkerName>(),It.IsAny<WorkerActivity>())).Callback<WorkerName,WorkerActivity>((n,a) =>
        {
            registry.Report(n,a);
            if (n == name && a == WorkerActivity.Waiting && entered.Task.IsCompleted) waiting.TrySetResult();
        });
        var delivery = new Mock<IDeliveryService>();
        delivery.Setup(s => s.ProcessNextAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken ct) =>
        {
            entered.TrySetResult(); await release.Task.WaitAsync(ct); return false;
        });
        var retention = new Mock<IEventLogRetentionService>();
        retention.Setup(s => s.CleanupAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken ct) =>
        {
            entered.TrySetResult(); await release.Task.WaitAsync(ct); return new EventLogRetentionResult(DateTimeOffset.UtcNow,0,1,false);
        });
        var queue = new EmailBackgroundQueue();
        await queue.QueueOtpEmailAsync(new("test@example.com","Test","123456"));
        var sender = new Mock<IEmailSenderService>();
        sender.Setup(s => s.SendOtpEmailAsync(It.IsAny<SendOtpEmailDto>(),It.IsAny<CancellationToken>())).Returns(async (SendOtpEmailDto _,CancellationToken ct) =>
        {
            entered.TrySetResult(); await release.Task.WaitAsync(ct); return true;
        });
        using var provider = new ServiceCollection().AddScoped(_ => delivery.Object).AddScoped(_ => retention.Object).BuildServiceProvider();
        using BackgroundService worker = name switch
        {
            WorkerName.Delivery => new DatabaseEmailWorker(provider.GetRequiredService<IServiceScopeFactory>(),new(),TimeProvider.System,
                NullLogger<DatabaseEmailWorker>.Instance,reporter.Object),
            WorkerName.EventLogRetention => new EventLogRetentionWorker(provider.GetRequiredService<IServiceScopeFactory>(),
                new() { Enabled = true },TimeProvider.System,NullLogger<EventLogRetentionWorker>.Instance,reporter.Object),
            _ => new EmailBackgroundWorker(queue,sender.Object,NullLogger<EmailBackgroundWorker>.Instance,reporter.Object)
        };
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Running",Worker(registry,name).Status);
            release.TrySetResult(); await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Waiting",Worker(registry,name).Status);
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await worker.StopAsync(timeout.Token);
        }
        Assert.Equal("Stopped",Worker(registry,name).Status);
    }

    [Fact]
    public async Task ActualDeliveryWorker_ReportsLoopFailure()
    {
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = Registry(TimeProvider.System);
        var reporter = new Mock<IWorkerHealthReporter>();
        reporter.Setup(r => r.Report(It.IsAny<WorkerName>(),It.IsAny<WorkerActivity>())).Callback<WorkerName,WorkerActivity>((n,a) =>
        {
            registry.Report(n,a); if (a == WorkerActivity.Failed) failed.TrySetResult();
        });
        var delivery = new Mock<IDeliveryService>();
        delivery.Setup(s => s.ProcessNextAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Private connection details"));
        using var provider = new ServiceCollection().AddScoped(_ => delivery.Object).BuildServiceProvider();
        using var worker = new DatabaseEmailWorker(provider.GetRequiredService<IServiceScopeFactory>(),new(),TimeProvider.System,
            NullLogger<DatabaseEmailWorker>.Instance,reporter.Object);
        await worker.StartAsync(CancellationToken.None);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Failed",Worker(registry,WorkerName.Delivery).Status);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await worker.StopAsync(timeout.Token);
    }
}
