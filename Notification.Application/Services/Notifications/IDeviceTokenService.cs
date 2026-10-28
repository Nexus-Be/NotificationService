
using Notification.Domain.Enums;

namespace Notification.Application.Services.Notifications;

public interface IDeviceTokenService
{
    // NOTI-01: Register or refresh device token
    Task<DeviceTokenDto> RegisterAsync(
        Guid userId,
        DevicePlatform platform,
        string token,
        CancellationToken cancellationToken = default
    );

    // NOTI-02: Revoke device token
    Task<bool> RevokeAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default
    );
}

public sealed record DeviceTokenDto(
    Guid DeviceId,
    Guid UserId,
    DevicePlatform Platform,
    bool IsActive,
    DateTimeOffset? LastSeenAt
);
