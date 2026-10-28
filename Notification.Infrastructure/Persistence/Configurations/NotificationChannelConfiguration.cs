namespace Notification.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;

public class NotificationChannelConfiguration : IEntityTypeConfiguration<NotificationChannel>
{
    public void Configure(EntityTypeBuilder<NotificationChannel> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Config).HasColumnType("jsonb");

        builder.HasMany(c => c.Templates)
            .WithOne(t => t.Channel)
            .HasForeignKey(t => t.ChannelCode)
            .HasPrincipalKey(c => c.Code);

        builder.HasMany(c => c.Preferences)
            .WithOne(p => p.Channel)
            .HasForeignKey(p => p.ChannelCode)
            .HasPrincipalKey(c => c.Code);

        builder.HasMany(c => c.Deliveries)
            .WithOne(d => d.Channel)
            .HasForeignKey(d => d.ChannelCode)
            .HasPrincipalKey(c => c.Code);
    }
}
