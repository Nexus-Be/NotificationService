
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Notification.Application.Services.Notifications;
using Notification.Domain.Enums;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Notification.Infrastructure.Services;

public sealed class RabbitMqHealthProbe : IRabbitMqHealthProbe
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMqHealthProbe> _logger;

    public RabbitMqHealthProbe(
        IConfiguration configuration,
        ILogger<RabbitMqHealthProbe> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task<RabbitMqProbeResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => CheckBroker(cancellationToken),
            cancellationToken);
    }

    private RabbitMqProbeResult CheckBroker(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Load RabbitMQ configuration
        var host =
            _configuration["RabbitMq:Host"] ?? "localhost";

        var port = int.TryParse(
            _configuration["RabbitMq:Port"],
            out var configuredPort)
            && configuredPort > 0
            && configuredPort <= 65535
                ? configuredPort
                : 5672;

        var username =
            _configuration["RabbitMq:User"] ?? "guest";

        var password =
            _configuration["RabbitMq:Password"] ?? "guest";

        var queueName =
            _configuration["RabbitMq:HealthQueue"]
            ?? "send-otp-email";

        // 2. Configure RabbitMQ connection
        var factory = new ConnectionFactory
        {
            HostName = host,
            Port = port,
            UserName = username,
            Password = password,
            VirtualHost = "/",
            RequestedConnectionTimeout =
                TimeSpan.FromSeconds(5),
            AutomaticRecoveryEnabled = false
        };

        try
        {
            // 3. Measure connection latency
            var stopwatch = Stopwatch.StartNew();

            using var connection =
                factory.CreateConnection();

            using var channel =
                connection.CreateModel();

            stopwatch.Stop();

            cancellationToken.ThrowIfCancellationRequested();

            var latencyMs = (int)Math.Min(
                int.MaxValue,
                stopwatch.ElapsedMilliseconds);

            // 4. Check monitored queue
            if (string.IsNullOrWhiteSpace(queueName))
            {
                return new RabbitMqProbeResult(
                    BrokerHealthStatus.Up,
                    latencyMs,
                    null);
            }

            try
            {
                // Passive check does not create the queue
                var queue = channel.QueueDeclarePassive(
                    queueName);

                var queueDepth = (int)Math.Min(
                    int.MaxValue,
                    (long)queue.MessageCount);

                return new RabbitMqProbeResult(
                    BrokerHealthStatus.Up,
                    latencyMs,
                    queueDepth);
            }
            catch (OperationInterruptedException ex)
            {
                _logger.LogWarning(
                    ex,
                    "RabbitMQ is reachable but queue " +
                    "{QueueName} cannot be inspected.",
                    queueName);

                return new RabbitMqProbeResult(
                    BrokerHealthStatus.Degraded,
                    latencyMs,
                    null);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "RabbitMQ broker health check failed.");

            return new RabbitMqProbeResult(
                BrokerHealthStatus.Down,
                null,
                null);
        }
    }
}
