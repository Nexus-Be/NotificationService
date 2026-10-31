namespace Notification.Application.Services.Notifications;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public sealed record ListNotificationsRequest(
    [property: Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")]
    int Page = 1,
    [property: Range(1, 100, ErrorMessage = "Page size must be between 1 and 100.")]
    int PageSize = 20
);

public sealed record MarkNotificationReadRequest(
    [property: Required(ErrorMessage = "Notification ID is required.")]
    Guid NotificationId
);

public sealed record RegisterDeviceRequest(
    [property: Required(ErrorMessage = "Device token is required.")]
    string Token,
    string Platform = "WEB"
);

public sealed record UpdatePreferenceItemRequest(
    string NotificationType,
    string ChannelCode,
    bool IsDisabled
);

public sealed record UpdatePreferencesRequest(
    IReadOnlyList<UpdatePreferenceItemRequest> Preferences
);

public sealed record GetMyNotificationsRequest(
    bool? IsRead = null,
    string? NotificationType = null,
    int PageIndex = 1,
    int PageSize = 20
);

public sealed record BatchMarkReadRequest(
    IReadOnlyList<Guid>? NotificationIds = null
);

public sealed record SearchNotificationHistoryRequest(
    Guid? UserId = null,
    string? NotificationType = null,
    string? Status = null,
    int PageIndex = 1,
    int PageSize = 20
);
