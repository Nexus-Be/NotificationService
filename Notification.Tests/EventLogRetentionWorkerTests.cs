namespace Notification.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Application;
using Notification.Application.Interfaces;
using Notification.Application.Services.EventLogs;
using Notification.Infrastructure;
using Notification.Infrastructure.BackgroundWorkers;
using Xunit;

public sealed class EventLogRetentionWorkerTests
{
    private static EventLogRetentionResult Result => new(DateTimeOffset.UtcNow.AddDays(-90),0,1,false);
    private static EventLogRetentionWorker Worker(ServiceProvider provider, EventLogRetentionSettings settings)
        => new(provider.GetRequiredService<IServiceScopeFactory>(),settings,TimeProvider.System,NullLogger<EventLogRetentionWorker>.Instance);

    [Fact]
    public async Task DisabledWorker_DoesNotResolveService_AndDisabledServiceDoesNotTouchDatabase()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var worker = Worker(provider,new());
        await worker.StartAsync(CancellationToken.None); await worker.StopAsync(CancellationToken.None);
        var db = new Mock<INotificationDbContext>(MockBehavior.Strict);
        Assert.Equal(0,(await new EventLogRetentionService(db.Object,new(),TimeProvider.System).CleanupAsync()).BatchesExecuted);
        db.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("BatchSize",0)]
    [InlineData("BatchSize",5001)]
    [InlineData("MaxBatchesPerRun",0)]
    [InlineData("MaxBatchesPerRun",101)]
    [InlineData("IntervalSeconds",0)]
    [InlineData("IntervalSeconds",86401)]
    [InlineData("RunTimeoutSeconds",0)]
    [InlineData("RunTimeoutSeconds",601)]
    public void Infrastructure_RejectsInvalidRetentionSettings(string property, int value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused",
            ["EventLogRetention:" + property] = value.ToString()
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructureServices(config));
    }

    [Fact]
    public async Task Registration_UsesDisabledDefault_BindsSettingsAndResolvesScopedServiceWithoutDbConnection()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused",
            ["EventLogRetention:BatchSize"] = "12", ["EventLogRetention:IntervalSeconds"] = "90"
        }).Build();
        var services = new ServiceCollection().AddLogging().AddApplicationServices().AddInfrastructureServices(config);
        Assert.Contains(services,d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(EventLogRetentionWorker));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var settings = provider.GetRequiredService<EventLogRetentionSettings>();
        Assert.False(settings.Enabled); Assert.Equal(12,settings.BatchSize); Assert.Equal(90,settings.IntervalSeconds);
        using var scope = provider.CreateScope();
        Assert.Equal(0,(await scope.ServiceProvider.GetRequiredService<IEventLogRetentionService>().CleanupAsync()).BatchesExecuted);
    }

    [Fact]
    public async Task DatabaseError_RetriesAtNextInterval_UsingAndDisposingFreshScopes()
    {
        var calls = 0; var disposed = 0;
        var secondDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var provider = new ServiceCollection().AddScoped<IEventLogRetentionService>(_ =>
            new DisposableService(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("Test database unavailable.");
                return Task.FromResult(Result);
            },() => { if (Interlocked.Increment(ref disposed) == 2) secondDisposed.TrySetResult(); })).BuildServiceProvider();
        using var worker = Worker(provider,new() { Enabled = true, IntervalSeconds = 1 });
        await worker.StartAsync(CancellationToken.None);
        await secondDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(timeout.Token);
        Assert.Equal(2,calls); Assert.Equal(2,disposed);
    }

    [Fact]
    public async Task Shutdown_CancelsActiveCleanupAndDisposesItsScope()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var disposed = 0;
        using var provider = new ServiceCollection().AddScoped<IEventLogRetentionService>(_ =>
            new DisposableService(async ct =>
            {
                entered.TrySetResult(); await Task.Delay(Timeout.Infinite,ct); return Result;
            },() => Interlocked.Increment(ref disposed))).BuildServiceProvider();
        using var worker = Worker(provider,new() { Enabled = true });
        await worker.StartAsync(CancellationToken.None); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(timeout.Token); Assert.Equal(1,disposed);
    }

    [Fact]
    public async Task RunTimeout_CancelsHungCleanupAndAllowsNextCycle()
    {
        var calls = 0; var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var provider = new ServiceCollection().AddScoped<IEventLogRetentionService>(_ => new DisposableService(async ct =>
        {
            if (Interlocked.Increment(ref calls) == 1) await Task.Delay(Timeout.Infinite,ct);
            retried.TrySetResult(); return Result;
        },() => { })).BuildServiceProvider();
        using var worker = Worker(provider,new() { Enabled = true, RunTimeoutSeconds = 1, IntervalSeconds = 1 });
        await worker.StartAsync(CancellationToken.None); await retried.Task.WaitAsync(TimeSpan.FromSeconds(7));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(timeout.Token); Assert.Equal(2,calls);
    }

    private sealed class DisposableService(Func<CancellationToken,Task<EventLogRetentionResult>> action, Action disposed)
        : IEventLogRetentionService, IDisposable
    {
        public Task<EventLogRetentionResult> CleanupAsync(CancellationToken ct = default) => action(ct);
        public void Dispose() => disposed();
    }
}
