
using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;

namespace Notification.Application.Services.Notifications;

public sealed class EventLogService : IEventLogService
{
    private readonly INotificationDbContext _db;

    public EventLogService(INotificationDbContext db)
    {
        _db = db;
    }

    // NOTI-13: Get event logs within 90 days
    public async Task<EventLogPageDto> GetAsync(
        string? sourceService,
        string? eventType,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate pagination
        if (pageIndex < 1 || pageSize < 1 || pageSize > 100)
        {
            throw new ArgumentException(
                "Invalid pagination parameters.");
        }

        var offset = ((long)pageIndex - 1) * pageSize;

        if (offset > int.MaxValue)
        {
            throw new ArgumentException(
                "Requested page exceeds supported range.");
        }

        // 2. Validate filters
        if ((sourceService is not null &&
             (string.IsNullOrWhiteSpace(sourceService) ||
              sourceService.Length > 100)) ||
            (eventType is not null &&
             (string.IsNullOrWhiteSpace(eventType) ||
              eventType.Length > 150)))
        {
            throw new ArgumentException(
                "Invalid event log filters.");
        }

        // 3. Limit query to last 90 days
        var cutoff = DateTimeOffset.UtcNow.AddDays(-90);

        var query = _db.EventLogs
            .AsNoTracking()
            .Where(e =>
                !e.IsDeleted &&
                e.OccurredAt >= cutoff);

        // 4. Filter by source service
        if (sourceService is not null)
        {
            var source = sourceService.Trim();

            query = query.Where(e =>
                e.SourceService == source);
        }

        // 5. Filter by event type
        if (eventType is not null)
        {
            var type = eventType.Trim();

            query = query.Where(e =>
                e.EventType == type);
        }

        // 6. Count records
        var total = await query.CountAsync(
            cancellationToken);

        // 7. Get paginated records
        var items = await query
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(e => new EventLogDto(
                e.Id,
                e.EventId,
                e.EventType,
                e.SourceService,
                e.ActorId,
                e.OccurredAt))
            .ToListAsync(cancellationToken);

        return new EventLogPageDto(
            items,
            total,
            pageIndex,
            pageSize);
    }

    // NOTI-14: Get event log details
    public async Task<EventLogDetailDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Event log ID must not be empty.",
                nameof(id));
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-90);

        var eventLog = await _db.EventLogs
            .AsNoTracking()
            .Where(e =>
                e.Id == id &&
                !e.IsDeleted &&
                e.OccurredAt >= cutoff)
            .Select(e => new EventLogDetailDto(
                e.Id,
                e.EventId,
                e.EventType,
                e.SourceService,
                e.ActorId,
                e.Payload,
                e.OccurredAt,
                e.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return eventLog;
    }
}
