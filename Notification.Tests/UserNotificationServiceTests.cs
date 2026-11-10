namespace Notification.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Services.Notifications;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class UserNotificationServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"NotificationDb_{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task SearchNotificationHistoryAsync_PreservesReadAt_WhenNotificationIsRead()
    {
        await using var db = CreateInMemoryDbContext();
        var service = new UserNotificationService(db);

        var userId = Guid.NewGuid();
        var readTime = DateTimeOffset.UtcNow.AddMinutes(-5);

        var notification = new NotificationRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Order Completed",
            Body = "Your order #1234 has been completed.",
            NotificationType = NotificationType.OrderPaid,
            Status = NotificationStatus.Completed,
            IsRead = true,
            ReadAt = readTime,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = readTime
        };

        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        var request = new SearchNotificationHistoryRequest(
            UserId: userId,
            NotificationType: null,
            Status: null,
            PageIndex: 1,
            PageSize: 20
        );

        var result = await service.SearchNotificationHistoryAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.True(item.IsRead);
        Assert.NotNull(item.ReadAt);
        Assert.Equal(readTime, item.ReadAt);
    }

    [Fact]
    public async Task GetMyNotificationsAsync_FiltersByUserIdAndIsRead()
    {
        await using var db = CreateInMemoryDbContext();
        var service = new UserNotificationService(db);

        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        db.Notifications.AddRange(
            new NotificationRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = "Unread 1",
                Body = "Body 1",
                IsRead = false,
                Status = NotificationStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
            },
            new NotificationRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = "Read 1",
                Body = "Body 2",
                IsRead = true,
                ReadAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Status = NotificationStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3)
            },
            new NotificationRecord
            {
                Id = Guid.NewGuid(),
                UserId = otherUserId,
                Title = "Other user unread",
                Body = "Body 3",
                IsRead = false,
                Status = NotificationStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        );
        await db.SaveChangesAsync();

        var request = new GetMyNotificationsRequest(IsRead: false, NotificationType: null, PageIndex: 1, PageSize: 20);
        var result = await service.GetMyNotificationsAsync(userId, request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("Unread 1", result.Items[0].Title);
        Assert.Equal(1, result.Pagination.TotalCount);
    }

    [Fact]
    public async Task MarkNotificationsAsReadAsync_MarksSpecificNotifications()
    {
        await using var db = CreateInMemoryDbContext();
        var service = new UserNotificationService(db);

        var userId = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        db.Notifications.AddRange(
            new NotificationRecord
            {
                Id = id1,
                UserId = userId,
                Title = "Noti 1",
                Body = "Body 1",
                IsRead = false,
                Status = NotificationStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new NotificationRecord
            {
                Id = id2,
                UserId = userId,
                Title = "Noti 2",
                Body = "Body 2",
                IsRead = false,
                Status = NotificationStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            }
        );
        await db.SaveChangesAsync();

        var request = new BatchMarkReadRequest(new List<Guid> { id1 });
        var result = await service.MarkNotificationsAsReadAsync(userId, request, CancellationToken.None);

        Assert.Equal(1, result.MarkedCount);

        var noti1 = await db.Notifications.FindAsync(id1);
        var noti2 = await db.Notifications.FindAsync(id2);
        Assert.True(noti1!.IsRead);
        Assert.NotNull(noti1.ReadAt);
        Assert.False(noti2!.IsRead);
    }

    [Fact]
    public async Task MarkNotificationsAsReadAsync_WithoutIds_MarksAllUserUnread()
    {
        await using var db = CreateInMemoryDbContext();
        var service = new UserNotificationService(db);

        var userId = Guid.NewGuid();

        db.Notifications.AddRange(
            new NotificationRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = "Noti 1",
                Body = "Body 1",
                IsRead = false,
                Status = NotificationStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new NotificationRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = "Noti 2",
                Body = "Body 2",
                IsRead = false,
                Status = NotificationStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            }
        );
        await db.SaveChangesAsync();

        var request = new BatchMarkReadRequest(null);
        var result = await service.MarkNotificationsAsReadAsync(userId, request, CancellationToken.None);

        Assert.Equal(2, result.MarkedCount);
    }

    [Fact]
    public async Task RegisterDeviceAsync_CreatesAndUpdatesDeviceToken()
    {
        await using var db = CreateInMemoryDbContext();
        var service = new UserNotificationService(db);

        var userId = Guid.NewGuid();
        var token = "fcm-token-xyz-123";

        // Register new device
        var request = new RegisterDeviceRequest(token, "ANDROID");
        var result = await service.RegisterDeviceAsync(userId, request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Android", result.Platform);
        Assert.Equal(token, result.Token);

        var deviceInDb = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token);
        Assert.NotNull(deviceInDb);
        Assert.Equal(userId, deviceInDb.UserId);
        Assert.True(deviceInDb.IsActive);

        // Revoke before switching accounts; active registration cannot be stolen.
        await service.RevokeDeviceAsync(userId, token, CancellationToken.None);
        // Update existing device with new user
        var newUserId = Guid.NewGuid();
        var updateRequest = new RegisterDeviceRequest(token, "IOS");
        var updateResult = await service.RegisterDeviceAsync(newUserId, updateRequest, CancellationToken.None);

        Assert.Equal("Ios", updateResult.Platform);
        var updatedInDb = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token);
        Assert.Equal(newUserId, updatedInDb!.UserId);
        Assert.Equal(DevicePlatform.Ios, updatedInDb.Platform);
    }

    [Fact]
    public async Task RevokeDeviceAsync_SetsIsActiveFalse()
    {
        await using var db = CreateInMemoryDbContext();
        var service = new UserNotificationService(db);

        var userId = Guid.NewGuid();
        var token = "token-to-revoke";
        db.DeviceTokens.Add(new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Platform = DevicePlatform.Web,
            Token = token,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await service.RevokeDeviceAsync(userId, token, CancellationToken.None);

        var revoked = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token);
        Assert.NotNull(revoked);
        Assert.False(revoked.IsActive);
    }

    [Fact]
    public async Task UpdatePreferencesAsync_And_GetPreferencesAsync_WorksCorrectly()
    {
        await using var db = CreateInMemoryDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = new UserNotificationService(db);

        var userId = Guid.NewGuid();

        var updateRequest = new UpdatePreferencesRequest(new List<UpdatePreferenceItemRequest>
        {
            new("OrderShipped", "Email", true),
            new("AuctionOutbid", "Push", false)
        });

        var updateResult = await service.UpdatePreferencesAsync(userId, updateRequest, CancellationToken.None);
        Assert.Equal(2, updateResult.UpdatedCount);

        var prefs = await service.GetPreferencesAsync(userId, CancellationToken.None);
        Assert.Equal(54, prefs.Count);
        Assert.Equal(2, prefs.Count(p => !p.IsDefault));
        Assert.Contains(prefs, p => p.NotificationType == "OrderShipped" && p.ChannelCode == "Email" && p.IsDisabled);
        Assert.Contains(prefs, p => p.NotificationType == "AuctionOutbid" && p.ChannelCode == "Push" && !p.IsDisabled);
    }

    [Fact]
    public async Task GetBrokerHealthAsync_ReturnsRecords()
    {
        await using var db = CreateInMemoryDbContext();
        var service = new UserNotificationService(db);

        db.BrokerHealthChecks.Add(new BrokerHealthCheck
        {
            Id = Guid.NewGuid(),
            BrokerName = "RabbitMQ Cluster",
            Status = BrokerHealthStatus.Up,
            LatencyMs = 12,
            QueueDepth = 5,
            Detail = "OK",
            CheckedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var health = await service.GetBrokerHealthAsync(10, CancellationToken.None);
        Assert.Single(health);
        Assert.Equal("RabbitMQ Cluster", health[0].BrokerName);
        Assert.Equal("Up", health[0].Status);
        Assert.Equal(12L, health[0].LatencyMs);
    }

    [Fact]
    public async Task RevokeDeviceAsync_RejectsOtherOwner_AndAuditsOwnRevocationOnce()
    {
        await using var db = CreateInMemoryDbContext();
        var owner = Guid.NewGuid();
        var device = new DeviceToken { Id = Guid.NewGuid(), UserId = owner, Platform = DevicePlatform.Web, Token = "private-device-token", IsActive = true };
        db.DeviceTokens.Add(device);
        await db.SaveChangesAsync();
        var service = new UserNotificationService(db);

        await service.RevokeDeviceAsync(Guid.NewGuid(), device.Token);
        Assert.True(device.IsActive);
        Assert.Empty(db.AuditLogs);

        await service.RevokeDeviceAsync(owner, device.Token);
        Assert.False(device.IsActive);
        var log = Assert.Single(db.AuditLogs);
        Assert.Equal(AuditAction.DeviceRevoke, log.Action);
        Assert.Equal(owner.ToString(), log.UserId);
        Assert.Equal(device.Id.ToString(), log.EntityId);
        Assert.False(log.IsSynced);
        Assert.DoesNotContain(device.Token, log.NewValues!);

        await service.RevokeDeviceAsync(owner, device.Token);
        await service.RevokeDeviceAsync(owner, "missing-token");
        Assert.Single(db.AuditLogs);
    }

    [Fact]
    public async Task RevokeDeviceAsync_DoesNotMutateDeletedDevice()
    {
        await using var db = CreateInMemoryDbContext();
        var owner = Guid.NewGuid();
        var device = new DeviceToken { Id = Guid.NewGuid(), UserId = owner, Platform = DevicePlatform.Web, Token = "deleted-token", IsActive = true, IsDeleted = true };
        db.DeviceTokens.Add(device);
        await db.SaveChangesAsync();
        await new UserNotificationService(db).RevokeDeviceAsync(owner, device.Token);
        Assert.True(device.IsActive);
        Assert.Empty(db.AuditLogs);
    }
}
