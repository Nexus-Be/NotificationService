using Notification.Domain.Base;

namespace Notification.Domain.Entities;

public class EventLog : BaseEntity<Guid>
{
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? EventId { get; set; }
    public required string EventType { get; set; }
    public required string SourceService { get; set; }
    public Guid? ActorId { get; set; }
    public string? ActorRole { get; set; }
    public Guid? TargetId { get; set; }
    public string? Payload { get; set; }
}
