namespace Notification.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;
public sealed class OrderPaidDispatchConfiguration : IEntityTypeConfiguration<OrderPaidDispatch>
{
    public void Configure(EntityTypeBuilder<OrderPaidDispatch> b)
    {
        b.HasKey(x => x.Id); b.HasQueryFilter(x => !x.IsDeleted);
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.OrderId).IsUnique(); b.HasIndex(x => x.BuyerNotificationId).IsUnique(); b.HasIndex(x => x.SellerNotificationId).IsUnique();
        b.HasOne<NotificationRecord>().WithMany().HasForeignKey(x => x.BuyerNotificationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<NotificationRecord>().WithMany().HasForeignKey(x => x.SellerNotificationId).OnDelete(DeleteBehavior.Restrict);
    }
}
