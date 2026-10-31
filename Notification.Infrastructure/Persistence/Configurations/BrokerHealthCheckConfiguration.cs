namespace Notification.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;

public class BrokerHealthCheckConfiguration : IEntityTypeConfiguration<BrokerHealthCheck>
{
    public void Configure(EntityTypeBuilder<BrokerHealthCheck> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BrokerName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Detail).HasMaxLength(1000);
    }
}
