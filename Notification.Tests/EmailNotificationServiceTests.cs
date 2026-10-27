namespace Notification.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Application.Common.Interfaces;
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
}
