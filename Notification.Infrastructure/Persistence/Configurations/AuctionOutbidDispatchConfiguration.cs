namespace Notification.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;
public sealed class AuctionOutbidDispatchConfiguration : IEntityTypeConfiguration<AuctionOutbidDispatch>
{
    public void Configure(EntityTypeBuilder<AuctionOutbidDispatch> b)
    {
        b.HasKey(x => x.Id); b.HasQueryFilter(x => !x.IsDeleted);
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.TriggerBidId).IsUnique(); b.HasIndex(x => x.PreviousBidId).IsUnique(); b.HasIndex(x => x.NotificationId).IsUnique();
        b.HasIndex(x => x.AuctionId);
        b.HasOne<NotificationRecord>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Restrict);
    }
}
