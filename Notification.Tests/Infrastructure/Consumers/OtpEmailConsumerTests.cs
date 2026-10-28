namespace Notification.Tests.Infrastructure.Consumers;

using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.Contracts.Events;
using Notification.Application.Common.Interfaces;
using Notification.Application.Consumers.User;
using Xunit;

public sealed class OtpEmailConsumerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Consume_succeeds_only_when_email_sender_succeeds(
        bool sendSucceeded)
    {
        var message = new SendOtpEmailIntegrationEvent(
            "recipient@example.com",
            "Test User",
            "123456",
            5);

        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        var context = new Mock<ConsumeContext<SendOtpEmailIntegrationEvent>>();
        context.SetupGet(x => x.Message).Returns(message);
        context.SetupGet(x => x.CancellationToken).Returns(token);

        var sender = new Mock<IEmailSenderService>(MockBehavior.Strict);
        sender.Setup(x => x.SendOtpRegistrationEmailAsync(
                message.Email,
                message.FullName,
                message.Otp,
                message.ExpiresInMinutes,
                token))
            .ReturnsAsync(sendSucceeded);

        var consumer = new SendOtpEmailConsumer(
            sender.Object,
            NullLogger<SendOtpEmailConsumer>.Instance);

        if (sendSucceeded)
        {
            await consumer.Consume(context.Object);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => consumer.Consume(context.Object));
        }

        sender.Verify(x => x.SendOtpRegistrationEmailAsync(
                message.Email,
                message.FullName,
                message.Otp,
                message.ExpiresInMinutes,
                token),
            Times.Once);
    }
}