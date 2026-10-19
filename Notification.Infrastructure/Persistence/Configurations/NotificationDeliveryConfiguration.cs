namespace Notification.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;

public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChannelCode).HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.Destination).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>();
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.Property(x => x.ErrorMessage).HasMaxLength(1000);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();

        builder.HasOne(d => d.Notification)
            .WithMany(n => n.Deliveries)
            .HasForeignKey(d => d.NotificationId);

        builder.HasOne(d => d.Channel)
            .WithMany(c => c.Deliveries)
            .HasForeignKey(d => d.ChannelCode)
            .HasPrincipalKey(c => c.Code);

        builder.HasMany(d => d.Logs)
            .WithOne(l => l.Delivery)
            .HasForeignKey(l => l.DeliveryId);
    }
}
