namespace Notification.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;

public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(150).IsRequired();
        builder.Property(x => x.SubjectTemplate).HasMaxLength(500);
        builder.Property(x => x.BodyTemplate).HasMaxLength(4000);
        builder.Property(x => x.Locale).HasMaxLength(20);
        builder.HasIndex(x => new { x.Code, x.Locale }).IsUnique();

        builder.HasOne(t => t.Channel)
            .WithMany(c => c.Templates)
            .HasForeignKey(t => t.ChannelCode)
            .HasPrincipalKey(c => c.Code);

        builder.HasMany(t => t.Notifications)
            .WithOne(n => n.Template)
            .HasForeignKey(n => n.TemplateId);
    }
}
