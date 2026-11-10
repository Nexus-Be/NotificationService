namespace Notification.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;
public sealed class ShipmentNotificationStreamConfiguration : IEntityTypeConfiguration<ShipmentNotificationStream>
{
    public void Configure(EntityTypeBuilder<ShipmentNotificationStream> b)
    { b.HasKey(x => x.Id); b.HasQueryFilter(x => !x.IsDeleted); b.Property(x => x.HeaderHash).HasMaxLength(64).IsRequired(); }
}
public sealed class ShipmentEventDispatchConfiguration : IEntityTypeConfiguration<ShipmentEventDispatch>
{
    public void Configure(EntityTypeBuilder<ShipmentEventDispatch> b)
    {
        b.HasKey(x => x.Id); b.HasQueryFilter(x => !x.IsDeleted); b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); b.Property(x => x.SuppressionReason).HasMaxLength(50);
        b.HasIndex(x => new { x.ShipmentId,x.SourceVersion }).IsUnique(); b.HasIndex(x => x.BuyerNotificationId).IsUnique(); b.HasIndex(x => x.SellerNotificationId).IsUnique();
        b.HasOne<ShipmentNotificationStream>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<NotificationRecord>().WithMany().HasForeignKey(x => x.BuyerNotificationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<NotificationRecord>().WithMany().HasForeignKey(x => x.SellerNotificationId).OnDelete(DeleteBehavior.Restrict);
    }
}
