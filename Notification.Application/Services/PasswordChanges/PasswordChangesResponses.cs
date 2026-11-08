namespace Notification.Application.Services.PasswordChanges;

public sealed record PasswordChangeResponse(Guid SourceEventId,Guid NotificationId,Guid DeliveryId,
    string NotificationStatus,string DeliveryStatus,bool IsReplay);
