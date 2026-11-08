namespace Notification.Application.Services.EventLogs;

public sealed class EventLogRetentionSettings
{
    public const int RetentionDays = 90;
    public bool Enabled { get; set; }
    public int BatchSize { get; set; } = 500;
    public int MaxBatchesPerRun { get; set; } = 20;
    public int IntervalSeconds { get; set; } = 3600;
    public int RunTimeoutSeconds { get; set; } = 120;
    public void Validate()
    {
        if (BatchSize is < 1 or > 5000 || MaxBatchesPerRun is < 1 or > 100 ||
            IntervalSeconds is < 1 or > 86400 || RunTimeoutSeconds is < 1 or > 600)
            throw new InvalidOperationException("Event log retention settings are outside the supported range.");
    }
}
