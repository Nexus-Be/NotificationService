namespace Notification.Application.Services.Deliveries;

public sealed class DeliveryWorkerSettings
{
    public bool Enabled { get; set; } = true;
    public int MaxAttempts { get; set; } = 3;
    public int BaseDelaySeconds { get; set; } = 10;
    public int PollIntervalSeconds { get; set; } = 5;
    public int AttemptTimeoutSeconds { get; set; } = 30;
    public int LeaseSeconds { get; set; } = 120;

    public void Validate()
    {
        if (MaxAttempts is < 1 or > 10 || BaseDelaySeconds is < 1 or > 3600 ||
            PollIntervalSeconds is < 1 or > 60 || AttemptTimeoutSeconds is < 1 or > 300 ||
            LeaseSeconds <= AttemptTimeoutSeconds + 10 || LeaseSeconds > 3600)
            throw new InvalidOperationException("Delivery worker settings are invalid; lease must exceed the send timeout by more than 10 seconds.");
    }
}
