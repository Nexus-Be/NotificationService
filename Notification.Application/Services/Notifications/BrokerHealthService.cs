
using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;

namespace Notification.Application.Services.Notifications;

// Contract for checking RabbitMQ connectivity
public interface IRabbitMqHealthProbe
{
    Task<RabbitMqProbeResult> CheckAsync(
        CancellationToken cancellationToken = default);
}

public sealed record RabbitMqProbeResult(
    BrokerHealthStatus Status,
    int? LatencyMs,
    int? QueueDepth
);

public sealed class BrokerHealthService
    : IBrokerHealthService
{
    private readonly INotificationDbContext _db;
    private readonly IRabbitMqHealthProbe _probe;

    public BrokerHealthService(
        INotificationDbContext db,
        IRabbitMqHealthProbe probe)
    {
        _db = db;
        _probe = probe;
    }

    // NOTI-16: Get RabbitMQ broker health
    public async Task<BrokerHealthDto> GetAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate limit
        if (limit < 1 || limit > 100)
        {
            throw new ArgumentException(
                "Limit must be between 1 and 100.",
                nameof(limit));
        }

        // 2. Check actual RabbitMQ connectivity
        var probeResult = await _probe.CheckAsync(
            cancellationToken);

        var now = DateTimeOffset.UtcNow;

        // 3. Save health check result
        var healthCheck = new BrokerHealthCheck
        {
            Id = Guid.NewGuid(),
            BrokerName = "rabbitmq",
            Status = probeResult.Status,
            LatencyMs = probeResult.LatencyMs,
            QueueDepth = probeResult.QueueDepth,
            CheckedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.BrokerHealthChecks.Add(healthCheck);

        await _db.SaveChangesAsync(cancellationToken);

        // 4. Get health check history
        var history = await _db.BrokerHealthChecks
            .AsNoTracking()
            .Where(h =>
                !h.IsDeleted &&
                h.BrokerName == "rabbitmq")
            .OrderByDescending(h => h.CheckedAt)
            .ThenByDescending(h => h.Id)
            .Take(limit)
            .Select(h => new BrokerHealthHistoryDto(
                h.Id,
                h.BrokerName,
                h.Status,
                h.LatencyMs,
                h.QueueDepth,
                h.CheckedAt))
            .ToListAsync(cancellationToken);

        // 5. Return broker health response
        return new BrokerHealthDto(
            probeResult.Status,
            now,
            probeResult.LatencyMs,
            probeResult.QueueDepth,
            history
        );
    }
}
