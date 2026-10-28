
using Microsoft.EntityFrameworkCore;
using Notification.Application.Services.Notifications;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Infrastructure.Persistence;
using Xunit;

namespace Notification.Tests.Application.Services.Notifications;

public class UserNotificationServiceTests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static NotificationRecord CreateNotification(
        Guid userId,
        bool isRead = false,
        bool isDeleted = false)
    {
        return new NotificationRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Test notification",
            Body = "Test content",
            IsRead = isRead,
            IsDeleted = isDeleted,
            Status = NotificationStatus.Pending
        };
    }

    [Fact]
    public async Task GetUnreadCountAsync_ShouldReturnFive_WhenUserHasFiveUnreadNotifications()
    {
        // Arrange
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            db.Notifications.Add(CreateNotification(userId));
        }

        await db.SaveChangesAsync();

        var service = new UserNotificationService(db);

        // Act
        var result = await service.GetUnreadCountAsync(userId);

        // Assert
        Assert.Equal(5, result);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ShouldReturnZero_WhenNoUnreadNotifications()
    {
        // Arrange
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();

        db.Notifications.Add(CreateNotification(userId, isRead: true));

        await db.SaveChangesAsync();

        var service = new UserNotificationService(db);

        // Act
        var result = await service.GetUnreadCountAsync(userId);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ShouldExcludeReadDeletedAndOtherUserNotifications()
    {
        // Arrange
        using var db = CreateDbContext();

        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        db.Notifications.AddRange(
            CreateNotification(userId),
            CreateNotification(userId),
            CreateNotification(userId, isRead: true),
            CreateNotification(userId, isDeleted: true),
            CreateNotification(otherUserId)
        );

        await db.SaveChangesAsync();

        var service = new UserNotificationService(db);

        // Act
        var result = await service.GetUnreadCountAsync(userId);

        // Assert
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ShouldThrowArgumentException_WhenUserIdIsEmpty()
    {
        // Arrange
        using var db = CreateDbContext();

        var service = new UserNotificationService(db);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetUnreadCountAsync(Guid.Empty)
        );
    }


    [Fact]
    public async Task MarkAllAsReadAsync_ShouldMarkAllUnreadNotifications()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();

        db.Notifications.AddRange(
            CreateNotification(userId),
            CreateNotification(userId),
            CreateNotification(userId)
        );

        await db.SaveChangesAsync();

        var service = new UserNotificationService(db);

        var result = await service.MarkAllAsReadAsync(userId);

        Assert.Equal(3, result);
        Assert.All(
            db.Notifications.ToList(),
            notification =>
            {
                Assert.True(notification.IsRead);
                Assert.NotNull(notification.ReadAt);
            });
    }

    [Fact]
    public async Task MarkAllAsReadAsync_ShouldReturnZero_WhenAllAlreadyRead()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();

        db.Notifications.Add(
            CreateNotification(userId, isRead: true)
        );

        await db.SaveChangesAsync();

        var service = new UserNotificationService(db);

        var result = await service.MarkAllAsReadAsync(userId);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_ShouldNotModifyOtherUsersOrDeletedNotifications()
    {
        using var db = CreateDbContext();

        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var valid = CreateNotification(userId);
        var deleted = CreateNotification(userId, isDeleted: true);
        var otherUser = CreateNotification(otherUserId);

        db.Notifications.AddRange(valid, deleted, otherUser);
        await db.SaveChangesAsync();

        var service = new UserNotificationService(db);

        var result = await service.MarkAllAsReadAsync(userId);

        Assert.Equal(1, result);
        Assert.True(valid.IsRead);
        Assert.False(deleted.IsRead);
        Assert.False(otherUser.IsRead);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_ShouldThrowArgumentException_WhenUserIdIsEmpty()
    {
        using var db = CreateDbContext();
        var service = new UserNotificationService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.MarkAllAsReadAsync(Guid.Empty)
        );
    }
}
