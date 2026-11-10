namespace Notification.Application.Services.SellerDecisions;

public sealed record SellerDecisionResponse(Guid SourceEventId,
                                            Guid ApplicationId,Guid NotificationId,
                                            Guid DeliveryId,
                                            string NotificationStatus,
                                            string DeliveryStatus,
                                            bool IsReplay);
