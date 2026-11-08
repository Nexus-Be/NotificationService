namespace Notification.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;

public sealed class SellerDecisionDispatchConfiguration : IEntityTypeConfiguration<SellerDecisionDispatch>
{
    public void Configure(EntityTypeBuilder<SellerDecisionDispatch> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.ApplicationId).IsUnique();
        builder.HasIndex(x => x.NotificationId).IsUnique();
        builder.HasOne<NotificationRecord>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Restrict);
    }
}
