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
    Promotion = 7,
    AuctionCancelled = 8,
    AuctionPaymentTimedOut = 9,
    AuctionSellerResult = 10,
    AuctionStarted = 11,
    OrderDecision = 12,
    OrderRefunded = 13,
    OrderCompleted = 14,
    OrderSellerSettled = 15,
    OrderPaymentExpired = 16,
    OrderBuyerCancelled = 17
}
