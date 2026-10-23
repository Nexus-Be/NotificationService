namespace Nexus.Contracts.Events;

public sealed record SendOtpEmailIntegrationEvent(
    string Email,
    string FullName,
    string Otp,
    int ExpiresInMinutes = 5);
