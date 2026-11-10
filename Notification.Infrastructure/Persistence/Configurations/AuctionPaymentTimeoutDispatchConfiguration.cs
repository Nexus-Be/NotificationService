namespace Notification.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;
public sealed class AuctionPaymentTimeoutDispatchConfiguration : IEntityTypeConfiguration<AuctionPaymentTimeoutDispatch>
{
    public void Configure(EntityTypeBuilder<AuctionPaymentTimeoutDispatch> b)
    {
        b.HasKey(x => x.Id); b.HasQueryFilter(x => !x.IsDeleted); b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.AuctionId).IsUnique(); b.HasIndex(x => x.ResultId).IsUnique(); b.HasIndex(x => x.WinningBidId).IsUnique();
        b.HasIndex(x => x.OrderId).IsUnique(); b.HasIndex(x => x.NotificationId).IsUnique();
        b.HasOne<NotificationRecord>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Restrict);
    }
}
