namespace Notification.Application.Services.AccountStatusChanges;

public sealed record AccountStatusChangeResponse(Guid SourceEventId, Guid UserId, long StatusVersion, Guid NotificationId, Guid DeliveryId,
    string NotificationStatus, string DeliveryStatus, bool IsReplay);
