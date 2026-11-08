namespace Notification.Application.Services.Health;

public interface IHealthService
{
    Task<HealthDetailsResponse> CheckAsync(CancellationToken cancellationToken = default);
}
public interface IDatabaseHealthProbe
{
    Task<DatabaseHealthResponse> CheckAsync(CancellationToken cancellationToken = default);
}
public enum WorkerActivity { NotStarted = 0, Running = 1, Waiting = 2, Failed = 3, Stopped = 4, Disabled = 5 }
public enum WorkerName { Delivery = 0, OtpEmail = 1, EventLogRetention = 2 }
public interface IWorkerHealthReporter
{
    void Report(WorkerName worker, WorkerActivity activity);
}
public interface IWorkerHealthReader
{
    IReadOnlyList<WorkerHealthResponse> Snapshot();
}
