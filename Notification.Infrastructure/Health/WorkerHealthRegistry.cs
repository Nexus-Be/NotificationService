namespace Notification.Infrastructure.Health;

using Notification.Application.Services.Deliveries;
using Notification.Application.Services.EventLogs;
using Notification.Application.Services.Health;
using Notification.Application.Services.Email;

public sealed class WorkerHealthRegistry : IWorkerHealthReader, IWorkerHealthReporter
{
    private sealed record State(bool Enabled, WorkerActivity Activity, DateTimeOffset? ActivityAt = null,
        DateTimeOffset? SuccessAt = null, DateTimeOffset? FailureAt = null, int Failures = 0);
    private readonly object _gate = new();
    private readonly Dictionary<WorkerName, State> _states;
    private readonly Dictionary<WorkerName, (TimeSpan Active, TimeSpan? Waiting)> _budgets;
    private readonly TimeProvider _time;
    public WorkerHealthRegistry(DeliveryWorkerSettings delivery, EventLogRetentionSettings retention, HealthSettings health, TimeProvider time, OtpWorkerSettings? otp = null)
    {
        delivery.Validate(); retention.Validate(); health.Validate(); _time = time;
        _states = new()
        {
            [WorkerName.Delivery] = new(delivery.Enabled,delivery.Enabled ? WorkerActivity.NotStarted : WorkerActivity.Disabled),
            [WorkerName.OtpEmail] = new(true,WorkerActivity.NotStarted),
            [WorkerName.EventLogRetention] = new(retention.Enabled,retention.Enabled ? WorkerActivity.NotStarted : WorkerActivity.Disabled)
        };
        _budgets = new()
        {
            [WorkerName.Delivery] = (TimeSpan.FromSeconds(Math.Max(delivery.LeaseSeconds,delivery.AttemptTimeoutSeconds + 30) + health.WorkerGraceSeconds),
                TimeSpan.FromSeconds(delivery.PollIntervalSeconds + health.WorkerGraceSeconds)),
            [WorkerName.OtpEmail] = (TimeSpan.FromSeconds(Math.Max(health.OtpStallSeconds, (otp?.SendTimeoutSeconds ?? 0) + health.WorkerGraceSeconds)),
                otp is null ? null : TimeSpan.FromSeconds(otp.PollIntervalSeconds + health.WorkerGraceSeconds)),
            [WorkerName.EventLogRetention] = (TimeSpan.FromSeconds(retention.RunTimeoutSeconds + health.WorkerGraceSeconds),
                TimeSpan.FromSeconds(retention.IntervalSeconds + health.WorkerGraceSeconds))
        };
    }
    public void Report(WorkerName worker, WorkerActivity activity)
    {
        lock (_gate)
        {
            var old = _states[worker];
            if (!old.Enabled) return;
            var now = _time.GetUtcNow().ToUniversalTime();
            _states[worker] = old with
            {
                Activity = activity, ActivityAt = now,
                SuccessAt = activity == WorkerActivity.Waiting ? now : old.SuccessAt,
                FailureAt = activity == WorkerActivity.Failed ? now : old.FailureAt,
                Failures = activity == WorkerActivity.Waiting ? 0 : activity == WorkerActivity.Failed ?
                    old.Failures < int.MaxValue ? old.Failures + 1 : int.MaxValue : old.Failures
            };
        }
    }
    public IReadOnlyList<WorkerHealthResponse> Snapshot()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow().ToUniversalTime();
            return _states.OrderBy(p => p.Key).Select(p =>
            {
                var value = p.Value; var status = value.Activity.ToString();
                var budget = value.Activity == WorkerActivity.Running ? _budgets[p.Key].Active :
                    value.Activity == WorkerActivity.Waiting ? _budgets[p.Key].Waiting : null;
                if (budget.HasValue && value.ActivityAt.HasValue && now - value.ActivityAt.Value > budget.Value) status = "Stalled";
                else if (value.Activity == WorkerActivity.Running && value.Failures > 0) status = "Recovering";
                return new WorkerHealthResponse(p.Key.ToString(),value.Enabled,status,value.ActivityAt,value.SuccessAt,value.FailureAt,value.Failures);
            }).ToList();
        }
    }
}
