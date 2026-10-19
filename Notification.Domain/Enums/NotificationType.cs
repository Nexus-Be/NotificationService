namespace Notification.Domain.Enums;

public enum NotificationType
{
    OrderPaid = 0,
    OrderShipped = 1,
    AuctionOutbid = 2,
    AuctionWon = 3,
    ReputationPenalty = 4,
    SecurityAlert = 5,
    Otp = 6,
    Promotion = 7
}
