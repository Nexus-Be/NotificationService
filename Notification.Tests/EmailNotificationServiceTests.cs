namespace Notification.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Application.Interfaces;
using Notification.Application.Services.Email;
using Xunit;

public sealed class EmailNotificationServiceTests
{
    [Fact]
    public async Task SendOtpEmailAsync_CallsUnderlyingSender_AndReturnsTrue()
    {
        // Arrange
        var mockSender = new Mock<IEmailSenderService>();
        mockSender
            .Setup(s => s.SendOtpRegistrationEmailAsync(
                "tester@nexus.local",
                "Tester",
                "654321",
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new EmailNotificationService(mockSender.Object);

        var dto = new SendOtpEmailDto("tester@nexus.local", "Tester", "654321", 10);

        // Act
        var result = await service.SendOtpEmailAsync(dto, CancellationToken.None);

        // Assert
        Assert.True(result);
        mockSender.Verify(s => s.SendOtpRegistrationEmailAsync(
            "tester@nexus.local",
            "Tester",
            "654321",
            10,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendEmailAsync_CallsUnderlyingSender_AndReturnsTrue()
    {
        // Arrange
        var mockSender = new Mock<IEmailSenderService>();
        mockSender
            .Setup(s => s.SendEmailAsync(
                "tester@nexus.local",
                "Welcome to Nexus",
                "<p>Hello World</p>",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new EmailNotificationService(mockSender.Object);

        var dto = new SendGeneralEmailDto("tester@nexus.local", "Welcome to Nexus", "<p>Hello World</p>");

        // Act
        var result = await service.SendEmailAsync(dto, CancellationToken.None);

        // Assert
        Assert.True(result);
        mockSender.Verify(s => s.SendEmailAsync(
            "tester@nexus.local",
            "Welcome to Nexus",
            "<p>Hello World</p>",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendOtpEmailAsync_WhenBackgroundQueueProvided_EnqueuesAndReturnsTrue()
    {
        // Arrange
        var mockSender = new Mock<IEmailSenderService>();
        var mockQueue = new Mock<IEmailBackgroundQueue>();
        mockQueue
            .Setup(q => q.QueueOtpEmailAsync(It.IsAny<SendOtpEmailDto>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var service = new EmailNotificationService(mockSender.Object, mockQueue.Object);
        var dto = new SendOtpEmailDto("queued@nexus.local", "Queued User", "112233", 5);

        // Act
        var result = await service.SendOtpEmailAsync(dto, CancellationToken.None);

        // Assert
        Assert.True(result);
        mockQueue.Verify(q => q.QueueOtpEmailAsync(dto, It.IsAny<CancellationToken>()), Times.Once);
        mockSender.Verify(s => s.SendOtpRegistrationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
