namespace Notification.Application.Services.Email;

public interface IOtpPayloadProtector
{
    string ProtectPayload(string value);
    string UnprotectPayload(string value);
    string ProtectFingerprint(string value);
    string UnprotectFingerprint(string value);
}
public interface IOtpEmailDispatchService
{
    Task<OtpEmailDispatchResponse> EnqueueAsync(SendOtpEmailRequest request,CancellationToken cancellationToken = default);
    Task<OtpDispatchStatusResponse> GetAsync(Guid id,CancellationToken cancellationToken = default);
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default);
}
public sealed record OtpDispatchStatusResponse(Guid RequestId,string MaskedRecipient,string Purpose,string Status,int AttemptCount,
    DateTimeOffset IssuedAt,DateTimeOffset ExpiresAt,DateTimeOffset? NextRetryAt,string? ErrorCode,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt);
