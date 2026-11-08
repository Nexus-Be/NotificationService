namespace Notification.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Api.Controllers.Public;
using Notification.Application.Common.Models;
using Notification.Application.Services.Notifications;
using Xunit;

public sealed class NotificationsControllerTests
{
    private readonly Mock<IUserNotificationService> _mockNotificationService;
    private readonly NotificationsController _controller;

    public NotificationsControllerTests()
    {
        _mockNotificationService = new Mock<IUserNotificationService>();
        _controller = new NotificationsController(
            _mockNotificationService.Object,
            NullLogger<NotificationsController>.Instance);
    }

    private void SetUserContext(Guid userId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["x-user-id"] = userId.ToString();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task RegisterDevice_ReturnsUnauthorized_WhenUserIdMissing()
    {
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var request = new RegisterDeviceRequest("token-123", "WEB");

        var result = await _controller.RegisterDevice(request, CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objResult.StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_ReturnsOk_WhenUserIdPresent()
    {
        var userId = Guid.NewGuid();
        SetUserContext(userId);
        var request = new RegisterDeviceRequest("token-123", "WEB");

        var expectedResponse = new DeviceRegisteredResponse("WEB", "token-123", DateTimeOffset.UtcNow);
        _mockNotificationService
            .Setup(s => s.RegisterDeviceAsync(userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var result = await _controller.RegisterDevice(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(expectedResponse, okResult.Value);
    }

    [Fact]
    public async Task RevokeDevice_ReturnsNoContent()
    {
        var userId = Guid.NewGuid();
        SetUserContext(userId);
        var token = "token-to-revoke";
        _mockNotificationService
            .Setup(s => s.RevokeDeviceAsync(userId, token, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.RevokeDevice(token, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _mockNotificationService.Verify(s => s.RevokeDeviceAsync(userId, token, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMyPreferences_ReturnsOk_WithPreferences()
    {
        var userId = Guid.NewGuid();
        SetUserContext(userId);

        var preferences = new List<NotificationPreferenceResponse>
        {
            new(Guid.NewGuid(), "OrderPaid", "Email", false)
        };

        _mockNotificationService
            .Setup(s => s.GetPreferencesAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preferences);

        var result = await _controller.GetMyPreferences(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(preferences, okResult.Value);
    }

    [Fact]
    public async Task UpdatePreferences_ReturnsOk_WithUpdateResult()
    {
        var userId = Guid.NewGuid();
        SetUserContext(userId);

        var request = new UpdatePreferencesRequest(new List<UpdatePreferenceItemRequest>
        {
            new("OrderPaid", "Email", true)
        });

        var expectedResult = new UpdatePreferencesResult(1);
        _mockNotificationService
            .Setup(s => s.UpdatePreferencesAsync(userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await _controller.UpdatePreferences(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(expectedResult, okResult.Value);
    }

    [Fact]
    public async Task GetMyNotifications_ReturnsOk_WithPagedData()
    {
        var userId = Guid.NewGuid();
        SetUserContext(userId);

        var items = new List<InAppNotificationItemResponse>
        {
            new(Guid.NewGuid(), userId, "Title", "Body", null, false, null, "OrderPaid", "Pending", DateTimeOffset.UtcNow)
        };
        var pagedData = new PagedData<InAppNotificationItemResponse>(items, PaginationMetadata.Create(1, 20, 1));

        _mockNotificationService
            .Setup(s => s.GetMyNotificationsAsync(userId, It.IsAny<GetMyNotificationsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedData);

        var result = await _controller.GetMyNotifications(false, "OrderPaid", 1, 20, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(pagedData, okResult.Value);
    }

    [Fact]
    public async Task MarkNotificationsAsRead_ReturnsOk_WithResult()
    {
        var userId = Guid.NewGuid();
        SetUserContext(userId);

        var request = new BatchMarkReadRequest(new List<Guid> { Guid.NewGuid() });
        var expectedResult = new BatchMarkReadResult(1);

        _mockNotificationService
            .Setup(s => s.MarkNotificationsAsReadAsync(userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await _controller.MarkNotificationsAsRead(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(expectedResult, okResult.Value);
    }
}
