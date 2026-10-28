
namespace Notification.Application.Services.Notifications;

public interface IEventLogService
{
    // NOTI-13: Get event logs
    Task<EventLogPageDto> GetAsync(
        string? sourceService,
        string? eventType,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default
    );

    // NOTI-14: Get event log details
    Task<EventLogDetailDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    );
}

// Event log summary
public sealed record EventLogDto(
    Guid Id,
    Guid? EventId,
    string EventType,
    string SourceService,
    Guid? ActorId,
    DateTimeOffset OccurredAt
);

// Paginated event logs
public sealed record EventLogPageDto(
    IReadOnlyList<EventLogDto> Items,
    int TotalCount,
    int PageIndex,
    int PageSize
);

// Event log details
public sealed record EventLogDetailDto(
    Guid Id,
    Guid? EventId,
    string EventType,
    string SourceService,
    Guid? ActorId,
    string? PayloadJson,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt
);
