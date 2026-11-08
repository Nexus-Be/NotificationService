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

public sealed record RegisterDeviceRequest
{
    [Required(ErrorMessage = "Device token is required."), StringLength(1000, ErrorMessage = "Device token must not exceed 1000 characters.")]
    public string Token { get; init; } = string.Empty;
    [Required(ErrorMessage = "Platform is required."), StringLength(20)]
    public string Platform { get; init; } = "Web";
    [StringLength(1000, ErrorMessage = "Previous token must not exceed 1000 characters.")]
    public string? PreviousToken { get; init; }

    [System.Text.Json.Serialization.JsonConstructor]
    public RegisterDeviceRequest() { }
    public RegisterDeviceRequest(string Token, string Platform = "Web", string? PreviousToken = null)
    { this.Token = Token; this.Platform = Platform; this.PreviousToken = PreviousToken; }
}

public sealed record UpdatePreferenceItemRequest(
    [Required(ErrorMessage = "Notification type is required.")] string NotificationType,
    [Required(ErrorMessage = "Channel code is required.")] string ChannelCode,
    [Required(ErrorMessage = "Disabled setting is required.")] bool? IsDisabled
);

public sealed record UpdatePreferencesRequest(
    [Required(ErrorMessage = "Preferences are required.")]
    [MinLength(1, ErrorMessage = "At least one preference is required.")]
    [MaxLength(100, ErrorMessage = "Preferences must not exceed 100 items.")]
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

public sealed record CreateInAppNotificationRequest(
    Guid UserId,
    string Title,
    string Body,
    string? NotificationType = null,
    string? Payload = null,
    Guid? SourceEventId = null
);
public sealed record SetNotificationReadStateRequest([Required(ErrorMessage = "Read state is required.")] bool? IsRead);
