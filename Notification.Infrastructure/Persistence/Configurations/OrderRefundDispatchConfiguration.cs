namespace Notification.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;
public sealed class OrderRefundDispatchConfiguration : IEntityTypeConfiguration<OrderRefundDispatch>
{
    public void Configure(EntityTypeBuilder<OrderRefundDispatch> b)
    {
        b.HasKey(x => x.Id); b.HasQueryFilter(x => !x.IsDeleted); b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.RefundId).IsUnique(); b.HasIndex(x => x.WalletCreditTransactionId).IsUnique(); b.HasIndex(x => x.NotificationId).IsUnique(); b.HasIndex(x => new { x.OrderKind,x.OrderId });
        b.HasOne<NotificationRecord>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Restrict);
    }
}
