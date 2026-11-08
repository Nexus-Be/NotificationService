namespace Notification.Application.Services.Management;

public sealed record ManagementActor(Guid UserId, string? TraceId = null, string? IpAddress = null, string? UserAgent = null);
