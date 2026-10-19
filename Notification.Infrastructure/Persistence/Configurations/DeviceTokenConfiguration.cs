namespace Notification.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;

public class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasConversion<string>();
        builder.Property(x => x.Token).HasMaxLength(1000).IsRequired();
        builder.HasIndex(x => x.Token).IsUnique();
    }
}
