
namespace Notification.Api.Controllers;

// NOTI-01: Register or refresh device token
public sealed class RegisterDeviceRequest
{
    public string? Platform { get; init; }

    public string? Token { get; init; }
}

// NOTI-02: Revoke device token
public sealed class RevokeDeviceRequest
{
    public string? Token { get; init; }
}
