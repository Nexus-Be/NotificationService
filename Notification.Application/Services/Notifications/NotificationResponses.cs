namespace Notification.Application.Services.Notifications;

using System;
using System.Collections.Generic;

public sealed record NotificationResponse(
    Guid Id,
    string? Title,
    string? Body,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt
);

public sealed record DeviceRegisteredResponse(
    string Platform,
    string Token,
    DateTimeOffset RegisteredAt
);

public sealed record NotificationPreferenceResponse(
    Guid Id,
    string NotificationType,
    string ChannelCode,
    bool IsDisabled
);

public sealed record UpdatePreferencesResult(
    int UpdatedCount
);

public sealed record BatchMarkReadResult(
    int MarkedCount
);

public sealed record InAppNotificationItemResponse(
    Guid Id,
    Guid UserId,
    string? Title,
    string? Body,
    string? Payload,
    bool IsRead,
    DateTimeOffset? ReadAt,
    string? NotificationType,
    string Status,
    DateTimeOffset CreatedAt
);

public sealed record BrokerHealthCheckResponse(
    Guid Id,
    string BrokerName,
    string Status,
    long LatencyMs,
    int QueueDepth,
    string? Detail,
    DateTimeOffset CheckedAt
);
