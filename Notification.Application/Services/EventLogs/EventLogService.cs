namespace Notification.Application.Services.EventLogs;

using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Common.Models;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Exceptions;

public sealed class EventLogService(INotificationDbContext db, TimeProvider time) : IEventLogService
{
    public async Task<EventLogAcceptanceResponse> CreateAsync(CreateEventLogRequest request, CancellationToken cancellationToken = default)
    {
        if (request.EventId is null || request.EventId == Guid.Empty || request.OccurredAt is null ||
            request.OccurredAt.Value == DateTimeOffset.MinValue || request.ActorId == Guid.Empty || request.TargetId == Guid.Empty ||
            request.SourceService is null || request.EventType is null) throw EventEnvelope.Invalid();
        var occurred = request.OccurredAt.Value.ToUniversalTime();
        occurred = new DateTimeOffset(occurred.Ticks / 10 * 10, TimeSpan.Zero);
        var row = new EventLog
        {
            Id = Guid.NewGuid(), EventId = request.EventId.Value, SourceService = EventEnvelope.Source(request.SourceService),
            EventType = EventEnvelope.Type(request.EventType), OccurredAt = occurred, ActorId = request.ActorId,
            ActorRole = EventEnvelope.Text(request.ActorRole, 50), TargetId = request.TargetId,
            TraceId = EventEnvelope.Text(request.TraceId, 100), Headers = EventEnvelope.Headers(request.Headers),
            Payload = EventEnvelope.CanonicalPayload(request.Payload),
            CreatedAt = time.GetUtcNow().ToUniversalTime()
        };
        row.CreatedAt = new DateTimeOffset(row.CreatedAt.Ticks / 10 * 10, TimeSpan.Zero);
        row.UpdatedAt = row.CreatedAt;
        var envelope = JsonSerializer.Serialize(new { row.SourceService, row.EventId, row.EventType, row.OccurredAt,
            row.ActorId, row.ActorRole, row.TargetId, row.TraceId, row.Headers, row.Payload });
        row.RequestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope)));
        var existing = await FindAsync(row, cancellationToken);
        if (existing is not null) return Replay(existing, row);
        db.EventLogs.Add(row);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
        {
            // SaveChanges rolls back the failed insert; detach before reading the winner.
            db.EventLogs.Remove(row);
            existing = await FindAsync(row, cancellationToken);
            if (existing is null) throw;
            return Replay(existing, row);
        }
        return new(row.Id, row.EventId!.Value, row.SourceService, row.CreatedAt, false);
    }
    private Task<EventLog?> FindAsync(EventLog row, CancellationToken ct) => db.EventLogs.IgnoreQueryFilters().AsNoTracking()
        .SingleOrDefaultAsync(e => e.SourceService == row.SourceService && e.EventId == row.EventId, ct);
    private static EventLogAcceptanceResponse Replay(EventLog old, EventLog input)
    {
        if (old.RequestHash is null || old.RequestHash != input.RequestHash)
            throw new DomainException("EVENT_LOG_CONFLICT", "Event key was already used for a different envelope or legacy record.", 409);
        return new(old.Id, old.EventId!.Value, old.SourceService, old.CreatedAt, true);
    }
    public async Task<PagedData<EventLogSummaryResponse>> ListAsync(ListEventLogsRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100 || (long)(request.Page - 1) * request.PageSize > int.MaxValue ||
            request.From > request.To || request.ActorId == Guid.Empty || request.TargetId == Guid.Empty || request.EventId == Guid.Empty)
            throw EventEnvelope.Invalid();
        var query = db.EventLogs.AsNoTracking().Where(e => !e.IsDeleted);
        if (request.SourceService is not null) { var source = EventEnvelope.Source(request.SourceService); query = query.Where(e => e.SourceService == source); }
        if (request.EventType is not null) { var type = EventEnvelope.Type(request.EventType); query = query.Where(e => e.EventType == type); }
        if (request.ActorId.HasValue) query = query.Where(e => e.ActorId == request.ActorId);
        if (request.TargetId.HasValue) query = query.Where(e => e.TargetId == request.TargetId);
        if (request.EventId.HasValue) query = query.Where(e => e.EventId == request.EventId);
        if (request.TraceId is not null) { var trace = EventEnvelope.Text(request.TraceId,100); query = query.Where(e => e.TraceId == trace); }
        if (request.From.HasValue) { var from = request.From.Value.ToUniversalTime(); query = query.Where(e => e.OccurredAt >= from); }
        if (request.To.HasValue) { var to = request.To.Value.ToUniversalTime(); query = query.Where(e => e.OccurredAt <= to); }
        var count = await query.CountAsync(cancellationToken);
        // SQL projection excludes potentially large payload and header columns.
        var items = await query.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(e => new EventLogSummaryResponse(e.Id,e.EventId,e.SourceService,e.EventType,e.OccurredAt,e.ActorId,
                e.ActorRole,e.TargetId,e.TraceId,e.CreatedAt)).ToListAsync(cancellationToken);
        return new(items, PaginationMetadata.Create(request.Page, request.PageSize, count));
    }
    public async Task<EventLogDetailResponse> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) throw EventEnvelope.Invalid();
        var row = await db.EventLogs.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id && !e.IsDeleted, cancellationToken);
        if (row is null) throw new DomainException("EVENT_LOG_NOT_FOUND", "Event log was not found.", 404);
        return new(new(row.Id,row.EventId,row.SourceService,row.EventType,row.OccurredAt,row.ActorId,row.ActorRole,
            row.TargetId,row.TraceId,row.CreatedAt), Parse(row.Payload), Parse(row.Headers));
    }
    private static JsonElement? Parse(string? json)
    {
        if (json is null) return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
