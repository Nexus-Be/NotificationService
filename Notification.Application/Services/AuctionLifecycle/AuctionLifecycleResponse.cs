namespace Notification.Application.Services.AuctionLifecycle;

public sealed record AuctionLifecycleResponse(Guid SourceEventId, Guid NotificationId);
