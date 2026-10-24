namespace Notification.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Contracts.Events;
using Notification.Application.Common.Interfaces;
using Notification.Infrastructure.Consumers;
using Notification.Infrastructure.Services.Email;
using Xunit;

public sealed class EmailSenderTests
{
    [Fact]
    public async Task SendOtpRegistrationEmailAsync_simulation_mode_when_credentials_empty()
    {
        var optionsMonitor = new FakeOptionsMonitor<SmtpOptions>(new SmtpOptions
        {
            Host = "smtp.gmail.com",
            Port = 587,
            Username = "",
            Password = ""
        });

        var emailSender = new MailKitEmailSenderService(optionsMonitor, NullLogger<MailKitEmailSenderService>.Instance);

        var result = await emailSender.SendOtpRegistrationEmailAsync(
            toEmail: "user@example.com",
            fullName: "Test User",
            otp: "123456",
            expiresInMinutes: 5);

        Assert.True(result);
    }

    [Fact]
    public async Task SendOtpEmailConsumer_consumes_message_and_calls_email_sender()
    {
        var mockSender = new Mock<IEmailSenderService>();
        mockSender
            .Setup(s => s.SendOtpRegistrationEmailAsync(
                "buyer@nexus.local",
                "Nguyen Van A",
                "888999",
                5,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var consumer = new SendOtpEmailConsumer(mockSender.Object, NullLogger<SendOtpEmailConsumer>.Instance);

        var message = new SendOtpEmailIntegrationEvent("buyer@nexus.local", "Nguyen Van A", "888999", 5);
        var mockContext = new Mock<ConsumeContext<SendOtpEmailIntegrationEvent>>();
        mockContext.SetupGet(c => c.Message).Returns(message);
        mockContext.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(mockContext.Object);

        mockSender.Verify(s => s.SendOtpRegistrationEmailAsync(
            "buyer@nexus.local",
            "Nguyen Van A",
            "888999",
            5,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class FakeOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public FakeOptionsMonitor(T currentValue) => CurrentValue = currentValue;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
