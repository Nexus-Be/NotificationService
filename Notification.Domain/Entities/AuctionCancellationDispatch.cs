namespace Notification.Domain.Entities;
using Notification.Domain.Base;
public sealed class AuctionCancellationDispatch : BaseEntity<Guid>
{
    public Guid AuctionId { get; set; }
    public required string RequestHash { get; set; }
}
