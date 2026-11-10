namespace Notification.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;
public sealed class AuctionCancellationDispatchConfiguration : IEntityTypeConfiguration<AuctionCancellationDispatch>
{
    public void Configure(EntityTypeBuilder<AuctionCancellationDispatch> b)
    {
        b.HasKey(x => x.Id); b.HasQueryFilter(x => !x.IsDeleted);
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); b.HasIndex(x => x.AuctionId).IsUnique();
    }
}
public sealed class AuctionCancellationRecipientConfiguration : IEntityTypeConfiguration<AuctionCancellationRecipient>
{
    public void Configure(EntityTypeBuilder<AuctionCancellationRecipient> b)
    {
        b.HasKey(x => x.Id); b.HasQueryFilter(x => !x.IsDeleted);
        b.HasIndex(x => new { x.SourceEventId,x.UserId }).IsUnique(); b.HasIndex(x => x.NotificationId).IsUnique();
        b.HasOne<AuctionCancellationDispatch>().WithMany().HasForeignKey(x => x.SourceEventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<NotificationRecord>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Restrict);
    }
}
