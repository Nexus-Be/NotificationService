namespace Notification.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Notification.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Application.Services.Deliveries;
using Notification.Infrastructure.BackgroundWorkers;
using Xunit;

public sealed class DatabaseEmailWorkerHostTests
{
    [Fact]
    public async Task DisabledWorker_DoesNotResolveOrProcessWork()
    {
        var service = new Mock<IDeliveryService>();
        using var provider = new ServiceCollection().AddScoped(_ => service.Object).BuildServiceProvider();
        using var worker = new DatabaseEmailWorker(provider.GetRequiredService<IServiceScopeFactory>(), new DeliveryWorkerSettings { Enabled = false }, TimeProvider.System, NullLogger<DatabaseEmailWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
        service.Verify(s => s.ProcessNextAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HostLoop_HandlesIterationFailureAndStopsDuringPolling()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Mock<IDeliveryService>();
        service.Setup(s => s.ProcessNextAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            entered.TrySetResult();
            return Task.FromException<bool>(new InvalidOperationException("Test database unavailable."));
        });
        using var provider = new ServiceCollection().AddScoped(_ => service.Object).BuildServiceProvider();
        using var worker = new DatabaseEmailWorker(provider.GetRequiredService<IServiceScopeFactory>(), new DeliveryWorkerSettings(), TimeProvider.System, NullLogger<DatabaseEmailWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(timeout.Token);
        service.Verify(s => s.ProcessNextAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("11")]
    public void Infrastructure_RejectsInvalidAttemptLimit(string limit)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused",
            ["NOTIFICATION_RETRY_LIMIT"] = limit
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructureServices(configuration));
    }
}
