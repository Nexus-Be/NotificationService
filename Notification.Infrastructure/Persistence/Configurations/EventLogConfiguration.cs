namespace Notification.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;

public class EventLogConfiguration : IEntityTypeConfiguration<EventLog>
{
    public void Configure(EntityTypeBuilder<EventLog> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(150).IsRequired();
        builder.Property(x => x.SourceService).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActorRole).HasMaxLength(50);
        builder.Property(x => x.Payload).HasColumnType("jsonb");
    }
}
