namespace Notification.Application.Services.EventLogs;

using System.Text.Json;

public sealed record EventLogAcceptanceResponse(Guid Id, Guid EventId, string SourceService, DateTimeOffset CreatedAt, bool IsReplay);
public sealed record EventLogSummaryResponse(Guid Id, Guid? EventId, string SourceService, string EventType,
    DateTimeOffset OccurredAt, Guid? ActorId, string? ActorRole, Guid? TargetId, string? TraceId, DateTimeOffset CreatedAt);
public sealed record EventLogDetailResponse(EventLogSummaryResponse Summary, JsonElement? Payload, JsonElement? Headers);
