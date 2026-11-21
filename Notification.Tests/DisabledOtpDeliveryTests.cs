namespace Notification.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Application.Interfaces;
using Notification.Application.Services.Email;
using Notification.Application.Services.Health;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.BackgroundWorkers;
using Notification.Infrastructure.Health;
using Xunit;

public sealed class DisabledOtpDeliveryTests
{
    [Fact]
    public async Task DisabledWorkerDoesNotResolveOrClaimOtpJobs()
    {
        var scopes=new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var health=new Mock<IWorkerHealthReporter>();
        using var worker=new DurableOtpWorker(scopes.Object,new(){Enabled=false},TimeProvider.System,
            NullLogger<DurableOtpWorker>.Instance,health.Object);
        await worker.StartAsync(CancellationToken.None);await worker.StopAsync(CancellationToken.None);
        scopes.Verify(s=>s.CreateScope(),Times.Never);
        health.Verify(h=>h.Report(WorkerName.OtpEmail,WorkerActivity.Disabled),Times.Once);
    }

    [Fact]
    public void DisabledWorkerIsReportedAsIntentionallyDisabledAndHealthy()
    {
        var registry=new WorkerHealthRegistry(new(){Enabled=false},new(){Enabled=false},new(),TimeProvider.System,new(){Enabled=false});
        var state=registry.Snapshot().Single(w=>w.Name==WorkerName.OtpEmail.ToString());
        Assert.False(state.Enabled);Assert.Equal("Disabled",state.Status);Assert.True(state.IsHealthy);
    }

    [Fact]
    public async Task DisabledOtpRequestReturns503WithoutEnqueueOrSmtp()
    {
        var dispatch=new Mock<IOtpEmailDispatchService>(MockBehavior.Strict);
        var sender=new Mock<IEmailSenderService>(MockBehavior.Strict);
        var service=new EmailNotificationService(sender.Object,otpDispatch:dispatch.Object,otpSettings:new(){Enabled=false});
        var ex=await Assert.ThrowsAsync<DomainException>(()=>service.DispatchOtpAsync(new(){Email="fixture@example.com",FullName="QA",OtpCode="123456",ExpiresInMinutes=5}));
        Assert.Equal(503,ex.StatusCode);Assert.Equal("OTP_DELIVERY_DISABLED",ex.ErrorCode);
        dispatch.VerifyNoOtherCalls();sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LegacyOtpSenderAlsoRejectsWhenDisabled()
    {
        var sender=new Mock<IEmailSenderService>(MockBehavior.Strict);
        var service=new EmailNotificationService(sender.Object,otpSettings:new(){Enabled=false});
        var ex=await Assert.ThrowsAsync<DomainException>(()=>service.SendOtpEmailAsync(new("fixture@example.com","QA","123456",5,"registration")));
        Assert.Equal(503,ex.StatusCode);sender.VerifyNoOtherCalls();
    }

    [Fact]
    public void ExistingOtpWorkerBehaviorRemainsEnabledByDefault()=>Assert.True(new OtpWorkerSettings().Enabled);
}
