namespace Notification.Domain.Entities;

using Notification.Domain.Base;
using Notification.Domain.Enums;

public sealed class OtpEmailDispatch : BaseEntity<Guid>
{
    public required string Recipient { get; set; }
    public required string RecipientKey { get; set; }
    public required string Purpose { get; set; }
    public required string FullName { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int RelativeMinutes { get; set; }
    public string? ProtectedPayload { get; set; }
    public required string ProtectedFingerprint { get; set; }
    public OtpDispatchStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public long RowVersion { get; set; } = 1;
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? TraceId { get; set; }
}
