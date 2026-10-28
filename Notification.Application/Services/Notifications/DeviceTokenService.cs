
using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;

namespace Notification.Application.Services.Notifications;

public sealed class DeviceTokenService : IDeviceTokenService
{
    private readonly INotificationDbContext _db;

    public DeviceTokenService(INotificationDbContext db)
    {
        _db = db;
    }

    // NOTI-01: Register or refresh device token
    public async Task<DeviceTokenDto> RegisterAsync(
        Guid userId,
        DevicePlatform platform,
        string token,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        ValidateToken(token);

        if (!Enum.IsDefined(platform))
        {
            throw new ArgumentException(
                "Invalid device platform.",
                nameof(platform));
        }

        token = token.Trim();

        var device = await _db.DeviceTokens
            .AsTracking()
            .SingleOrDefaultAsync(
                d => d.Token == token,
                cancellationToken);

        var now = DateTimeOffset.UtcNow;

        if (device is null)
        {
            device = new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Platform = platform,
                Token = token,
                IsActive = true,
                LastSeenAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.DeviceTokens.Add(device);
        }
        else
        {
            if (device.UserId != userId && device.IsActive
                && !device.IsDeleted)
            {
                throw new InvalidOperationException(
                    "Device token belongs to another active user.");
            }

            device.UserId = userId;
            device.Platform = platform;
            device.IsActive = true;
            device.IsDeleted = false;
            device.DeletedAt = null;
            device.LastSeenAt = now;
            device.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new DeviceTokenDto(
            device.Id,
            device.UserId,
            device.Platform,
            device.IsActive,
            device.LastSeenAt
        );
    }

    // NOTI-02: Revoke device token
    public async Task<bool> RevokeAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        ValidateToken(token);

        token = token.Trim();

        var device = await _db.DeviceTokens
            .AsTracking()
            .SingleOrDefaultAsync(
                d => d.UserId == userId
                     && d.Token == token
                     && !d.IsDeleted,
                cancellationToken);

        if (device is null)
            return false;

        if (device.IsActive)
        {
            device.IsActive = false;
            device.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID must not be empty.",
                nameof(userId));
        }
    }

    private static void ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)
            || token.Trim().Length > 1000)
        {
            throw new ArgumentException(
                "Device token is invalid.",
                nameof(token));
        }
    }
}
