
using Notification.Domain.Enums;

namespace Notification.Application.Services.Notifications;

public interface IBrokerHealthService
{
    // NOTI-16: Get RabbitMQ broker health
    Task<BrokerHealthDto> GetAsync(
        int limit = 50,
        CancellationToken cancellationToken = default
    );
}

// Current broker health response
public sealed record BrokerHealthDto(
    BrokerHealthStatus CurrentStatus,
    DateTimeOffset? LastCheckedAt,
    int? LatestLatencyMs,
    int? LatestQueueDepth,
    IReadOnlyList<BrokerHealthHistoryDto> History
);

// Historical broker health record
public sealed record BrokerHealthHistoryDto(
    Guid Id,
    string BrokerName,
    BrokerHealthStatus Status,
    int? LatencyMs,
    int? QueueDepth,
    DateTimeOffset CheckedAt
);
