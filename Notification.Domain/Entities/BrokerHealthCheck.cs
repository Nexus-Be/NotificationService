using Notification.Domain.Base;
using Notification.Domain.Enums;

namespace Notification.Domain.Entities;

public class BrokerHealthCheck : BaseEntity<Guid>
{
    public required string BrokerName { get; set; } = "rabbitmq";
    public required BrokerHealthStatus Status { get; set; } = BrokerHealthStatus.Up;
    public int? LatencyMs { get; set; }
    public int? QueueDepth { get; set; }
    public string? Detail { get; set; }
    public required DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
}
