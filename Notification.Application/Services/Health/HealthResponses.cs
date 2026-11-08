namespace Notification.Application.Services.Health;

using System.Text.Json.Serialization;

public sealed record LivenessResponse(string Status = "Healthy", string Service = "notification-service");
public sealed record ReadinessResponse(string Status, string Service, DateTimeOffset CheckedAt, string Database, string Workers);
public sealed record DatabaseHealthResponse(string Status, string? ErrorCode)
{
    [JsonIgnore] public bool IsHealthy => Status == "Healthy";
}
public sealed record WorkerHealthResponse(string Name, bool Enabled, string Status, DateTimeOffset? LastActivityAt,
    DateTimeOffset? LastSuccessAt, DateTimeOffset? LastFailureAt, int ConsecutiveFailures)
{
    [JsonIgnore] public bool IsHealthy => Status is "Running" or "Waiting" or "Disabled";
}
public sealed record HealthDetailsResponse(string Status, string Service, DateTimeOffset CheckedAt,
    DatabaseHealthResponse Database, IReadOnlyList<WorkerHealthResponse> Workers)
{
    [JsonIgnore] public bool IsReady => Status == "Healthy";
    public ReadinessResponse ToReadiness() => new(IsReady ? "Ready" : "NotReady",Service,CheckedAt,Database.Status,
        Workers.All(w => w.IsHealthy) ? "Healthy" : "Unhealthy");
}
