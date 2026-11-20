namespace Notification.Tests;

using Notification.Domain.Enums;
using Xunit;

/// <summary>
/// T23: other services send these types by name or number. A value that moves or disappears breaks them silently (the sender gets a 400
/// and the user never sees the message), so the numbers are pinned here.
/// </summary>
public sealed class NotificationTypeContractTests
{
    [Theory]
    [InlineData(NotificationType.AuctionWatchlistBid, 20)]
    [InlineData(NotificationType.AuctionReportResolved, 21)]
    [InlineData(NotificationType.AuctionDeposit, 22)]
    [InlineData(NotificationType.AuctionReserveApprovalNeeded, 23)]
    [InlineData(NotificationType.AuctionReserveRestriction, 24)]
    [InlineData(NotificationType.WithdrawalResult, 25)]
    [InlineData(NotificationType.ProductBannedByReport, 26)]
    [InlineData(NotificationType.ProductReportResolved, 27)]
    public void ValuesUsedByOtherServicesDoNotMove(NotificationType type, int expected)
    {
        Assert.Equal(expected, (int)type);
    }
}
