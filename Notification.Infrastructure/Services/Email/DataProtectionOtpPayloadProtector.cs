namespace Notification.Infrastructure.Services.Email;

using Microsoft.AspNetCore.DataProtection;
using Notification.Application.Services.Email;

public sealed class DataProtectionOtpPayloadProtector : IOtpPayloadProtector
{
    private readonly IDataProtector _payload;
    private readonly IDataProtector _fingerprint;
    public DataProtectionOtpPayloadProtector(IDataProtectionProvider provider)
    {
        _payload = provider.CreateProtector("Notification.Otp.Payload.v1");
        _fingerprint = provider.CreateProtector("Notification.Otp.Fingerprint.v1");
    }
    public string ProtectPayload(string value) => _payload.Protect(value);
    public string UnprotectPayload(string value) => _payload.Unprotect(value);
    public string ProtectFingerprint(string value) => _fingerprint.Protect(value);
    public string UnprotectFingerprint(string value) => _fingerprint.Unprotect(value);
}
