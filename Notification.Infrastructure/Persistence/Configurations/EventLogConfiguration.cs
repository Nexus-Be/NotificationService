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
        builder.Property(x => x.Headers).HasColumnType("jsonb");
        builder.Property(x => x.TraceId).HasMaxLength(100);
        builder.Property(x => x.RequestHash).HasMaxLength(64);
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.HasIndex(x => new { x.SourceService, x.EventId }).IsUnique().HasFilter("\"EventId\" IS NOT NULL");
        builder.HasIndex(x => new { x.OccurredAt, x.Id });
        builder.HasIndex(x => new { x.SourceService, x.OccurredAt });
        builder.HasIndex(x => new { x.EventType, x.OccurredAt });
        builder.HasIndex(x => new { x.ActorId, x.OccurredAt });
        builder.HasIndex(x => x.TraceId);
        builder.HasIndex(x => new { x.CreatedAt, x.Id });
    }
}
