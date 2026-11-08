namespace Notification.Application.Services.Management;

public sealed record TemplateResponse(Guid Id, string Code, string Locale, string NotificationType, string ChannelCode,
    string? SubjectTemplate, string? BodyTemplate, bool IsMandatory, bool IsActive, DateTimeOffset UpdatedAt);
public sealed record ChannelResponse(Guid Id, string Code, string Name, string? Provider, bool IsDisabled, DateTimeOffset UpdatedAt);
public sealed record DeliveryResponse(Guid Id, Guid NotificationId, Guid UserId, string Channel, string Status,
    int AttemptCount, DateTimeOffset? LastAttemptAt, DateTimeOffset? NextRetryAt, string? ErrorCode,
    long RowVersion, string DestinationHint, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record DeliveryDetailResponse(DeliveryResponse Delivery, string NotificationStatus, string? TemplateCode);
public sealed record DeliveryLogResponse(Guid Id, string Action, string Level, string Message, int? Attempt,
    string? ErrorCode, DateTimeOffset? NextRetryAt, string? ProviderMessageId, Guid? ManualRetryRequestId, DateTimeOffset CreatedAt);
public sealed record RetryAcceptanceResponse(Guid RequestId, Guid DeliveryId, long AcceptedRowVersion, DateTimeOffset AcceptedAt, bool IsReplay);
