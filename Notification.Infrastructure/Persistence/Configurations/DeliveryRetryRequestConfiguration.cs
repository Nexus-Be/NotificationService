namespace Notification.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;

public sealed class DeliveryRetryRequestConfiguration : IEntityTypeConfiguration<DeliveryRetryRequest>
{
    public void Configure(EntityTypeBuilder<DeliveryRetryRequest> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.HasQueryFilter(r => !r.IsDeleted);
        builder.HasIndex(r => new { r.DeliveryId, r.CreatedAt });
        builder.HasOne(r => r.Delivery).WithMany().HasForeignKey(r => r.DeliveryId).OnDelete(DeleteBehavior.Restrict);
    }
}
