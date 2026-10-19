namespace Notification.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NotificationType).HasConversion<string>();
        builder.Property(x => x.ChannelCode).HasConversion<string>().HasMaxLength(50);

        builder.HasOne(p => p.Channel)
            .WithMany(c => c.Preferences)
            .HasForeignKey(p => p.ChannelCode)
            .HasPrincipalKey(c => c.Code);
    }
}
