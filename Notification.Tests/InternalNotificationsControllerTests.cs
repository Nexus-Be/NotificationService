namespace Notification.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Notification.Api.Controllers.Internal;
using Notification.Application.Services.Notifications;
using Xunit;

public sealed class InternalNotificationsControllerTests
{
    private readonly Mock<IUserNotificationService> _notificationServiceMock;
    private readonly Mock<ILogger<InternalNotificationsController>> _loggerMock;
    private readonly InternalNotificationsController _controller;

    public InternalNotificationsControllerTests()
    {
        _notificationServiceMock = new Mock<IUserNotificationService>();
        _loggerMock = new Mock<ILogger<InternalNotificationsController>>();
        _controller = new InternalNotificationsController(_notificationServiceMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CreateInAppNotification_WithValidRequest_ReturnsOkResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new CreateInAppNotificationRequest(
            userId,
            "Order Paid",
            "Your order has been paid successfully.",
            "OrderPaid",
            "{\"orderId\":\"123\"}",
            Guid.NewGuid()
        );

        var expectedResponse = new InAppNotificationItemResponse(
            Guid.NewGuid(),
            userId,
            request.Title,
            request.Body,
            request.Payload,
            false,
            null,
            request.NotificationType,
            "Completed",
            DateTimeOffset.UtcNow
        );

        _notificationServiceMock
            .Setup(s => s.CreateInAppNotificationAsync(It.IsAny<CreateInAppNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.CreateInAppNotification(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task CreateInAppNotification_WhenUserDisabledPreference_ReturnsSkippedResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new CreateInAppNotificationRequest(
            userId,
            "Auction Outbid",
            "You have been outbid.",
            "AuctionOutbid",
            null,
            null
        );

        // Simulate user disabled this notification preference (returns null)
        _notificationServiceMock
            .Setup(s => s.CreateInAppNotificationAsync(It.IsAny<CreateInAppNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InAppNotificationItemResponse?)null);

        // Act
        var result = await _controller.CreateInAppNotification(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task CreateInAppNotification_WithEmptyTitle_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateInAppNotificationRequest(
            Guid.NewGuid(),
            string.Empty,
            "Body content",
            null,
            null,
            null
        );

        // Act
        var result = await _controller.CreateInAppNotification(request, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }
}