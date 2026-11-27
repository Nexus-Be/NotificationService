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
    OrderBuyerCancelled = 17,
    AuctionFailed = 18,
    AuctionWinnerOffer = 19,
    AuctionWatchlistBid = 20,
    AuctionReportResolved = 21,
    AuctionDeposit = 22,
    AuctionReserveApprovalNeeded = 23,
    AuctionReserveRestriction = 24,
    // Values 0-24 are in use and must never be renumbered.
    WithdrawalResult = 25,
    ProductBannedByReport = 26,
    /// <summary>Told to the person who reported a product once staff have dealt with the report.</summary>
    ProductReportResolved = 27,
    ProductReportDismissed = 28,
    ProductApproved = 29,
    ProductRejected = 30,
    ProductSuspended = 31,
    ProductBanned = 32,
    NewProductReview = 33,
    ReviewHidden = 34
}
