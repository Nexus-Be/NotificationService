namespace Notification.Application.Services.Health;

public sealed class HealthSettings
{
    public int DatabaseTimeoutSeconds { get; set; } = 3;
    public int WorkerGraceSeconds { get; set; } = 30;
    public int OtpStallSeconds { get; set; } = 120;
    public void Validate()
    {
        if (DatabaseTimeoutSeconds is < 1 or > 15 || WorkerGraceSeconds is < 5 or > 300 || OtpStallSeconds is < 10 or > 1800)
            throw new InvalidOperationException("Health settings are outside the supported range.");
    }
}
