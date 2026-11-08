namespace Notification.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Entities;

public sealed class OtpEmailDispatchConfiguration : IEntityTypeConfiguration<OtpEmailDispatch>
{
    public void Configure(EntityTypeBuilder<OtpEmailDispatch> b)
    {
        b.HasKey(x => x.Id); b.HasQueryFilter(x => !x.IsDeleted);
        b.Property(x => x.Recipient).HasMaxLength(254).IsRequired();
        b.Property(x => x.RecipientKey).HasMaxLength(254).IsRequired();
        b.Property(x => x.Purpose).HasMaxLength(40).IsRequired();
        b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ErrorCode).HasMaxLength(100);
        b.Property(x => x.TraceId).HasMaxLength(100);
        b.Property(x => x.ProtectedPayload).HasMaxLength(16000);
        b.Property(x => x.ProtectedFingerprint).HasMaxLength(1000).IsRequired();
        b.Property(x => x.RowVersion).IsConcurrencyToken();
        b.HasIndex(x => new { x.RecipientKey,x.Purpose,x.IssuedAt });
        b.HasIndex(x => new { x.Status,x.NextRetryAt });
        b.HasIndex(x => x.ExpiresAt);
    }
}
