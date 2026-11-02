namespace Notification.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Api.Controllers.Public;
using Notification.Application.Common.Models;
using Notification.Application.Services.Notifications;
using Xunit;

public sealed class AdminControllersTests
{
    private readonly Mock<IUserNotificationService> _mockNotificationService;
    private readonly AdminNotificationsController _adminNotificationsController;
    private readonly AdminHealthController _adminHealthController;

    public AdminControllersTests()
    {
        _mockNotificationService = new Mock<IUserNotificationService>();
        _adminNotificationsController = new AdminNotificationsController(
            _mockNotificationService.Object,
            NullLogger<AdminNotificationsController>.Instance);
        _adminHealthController = new AdminHealthController(
            _mockNotificationService.Object,
            NullLogger<AdminHealthController>.Instance);
    }

    [Fact]
    public async Task SearchNotificationHistory_ReturnsOk_WithPagedData()
    {
        var items = new List<InAppNotificationItemResponse>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Admin Title", "Admin Body", null, true, DateTimeOffset.UtcNow, "System", "Completed", DateTimeOffset.UtcNow)
        };
        var pagedData = new PagedData<InAppNotificationItemResponse>(items, PaginationMetadata.Create(1, 20, 1));

        _mockNotificationService
            .Setup(s => s.SearchNotificationHistoryAsync(It.IsAny<SearchNotificationHistoryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedData);

        var result = await _adminNotificationsController.SearchNotificationHistory(
            userId: null,
            notificationType: null,
            status: null,
            page: 1,
            pageSize: 20,
            cancellationToken: CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(pagedData, okResult.Value);
    }

    [Fact]
    public async Task GetBrokerHealth_ReturnsOk_WithRecords()
    {
        var records = new List<BrokerHealthCheckResponse>
        {
            new(Guid.NewGuid(), "Broker1", "Up", 15L, 0, null, DateTimeOffset.UtcNow)
        };

        _mockNotificationService
            .Setup(s => s.GetBrokerHealthAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var result = await _adminHealthController.GetBrokerHealth(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(records, okResult.Value);
    }
}
