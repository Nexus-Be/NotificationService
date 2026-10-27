namespace Notification.Application.Services.Notifications;

public sealed record NotificationDto(
    Guid Id,
    string? Title,
    string? Body,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt
);

public sealed record NotificationPageDto(
    IReadOnlyList<NotificationDto> Items,
    int Total,
    int PageIndex,
    int PageSize
);