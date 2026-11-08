namespace Notification.Application.Services.Email;

public sealed class OtpWorkerSettings
{
    public int PollIntervalSeconds { get; set; } = 1;
    public int SendTimeoutSeconds { get; set; } = 30;
    public int LeaseSeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 3;
    public int BaseDelaySeconds { get; set; } = 5;
    public void Validate()
    {
        if (PollIntervalSeconds is < 1 or > 30 || SendTimeoutSeconds is < 1 or > 120 ||
            LeaseSeconds <= SendTimeoutSeconds + 10 || LeaseSeconds > 600 || MaxAttempts is < 1 or > 3 || BaseDelaySeconds is < 1 or > 60)
            throw new InvalidOperationException("OTP worker settings are invalid; lease must exceed the send timeout by more than 10 seconds.");
    }
}
