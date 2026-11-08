namespace Notification.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.HasIndex(x => new { x.UserId, x.NotificationType, x.ChannelCode }).IsUnique().HasFilter("NOT \"IsDeleted\"");

        builder.HasOne(p => p.Channel)
            .WithMany(c => c.Preferences)
            .HasForeignKey(p => p.ChannelCode)
            .HasPrincipalKey(c => c.Code);
    }
}
