namespace Notification.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Message).HasMaxLength(2000);
        builder.Property(x => x.Metadata).HasColumnType("jsonb");

        builder.HasOne(l => l.Notification)
            .WithMany(n => n.Logs)
            .HasForeignKey(l => l.NotificationId);

        builder.HasOne(l => l.Delivery)
            .WithMany(d => d.Logs)
            .HasForeignKey(l => l.DeliveryId);
    }
}
