namespace Notification.Tests;

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Notification.Infrastructure.BackgroundWorkers;
using Moq;
using Npgsql;
using Notification.Api.Common;
using Notification.Api.Controllers.Public;
using Notification.Api.Extensions;
using Notification.Api.Middleware;
using Notification.Application;
using Notification.Application.Interfaces;
using Notification.Application.Services.Health;
using Notification.Infrastructure;
using Notification.Infrastructure.Health;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class HealthApiIntegrationTests : IAsyncLifetime
{
    private const string Detail = "/api/v1/admin/notifications/health-checks";
    private string? _admin, _database, _connection;
    private WebApplication? _app;
    private HttpClient _client = null!;
    private readonly Mock<IEmailSenderService> _sender = new(MockBehavior.Strict);
    private readonly Clock _clock = new();
    private WorkerHealthRegistry Registry => _app!.Services.GetRequiredService<WorkerHealthRegistry>();
    private AppDbContext Db(string? connection = null) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection ?? _connection).Options);
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_health_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ConnectionStrings:DefaultConnection"] = _connection, ["DeliveryWorker:Enabled"] = "false",
            ["EventLogRetention:Enabled"] = "false", ["Health:DatabaseTimeoutSeconds"] = "3", ["OtpWorker:PollIntervalSeconds"] = "30"
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(HealthController).Assembly)
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiValidationProblemFactory.Create);
        builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen();
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddApplicationServices().AddInfrastructureServices(builder.Configuration);
        builder.Services.AddSingleton(_sender.Object);
        _app = builder.Build();
        _app.UseGlobalExceptionHandler(); _app.UseMiddleware<UserContextMiddleware>(); _app.UseSwagger();
        _app.MapControllers(); _app.MapNotificationHealth();
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    }
    public async Task DisposeAsync()
    {
        _client?.Dispose(); if (_app is not null) await _app.DisposeAsync();
        _sender.VerifyNoOtherCalls();
        if (_database is null || _admin is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("DROP DATABASE \"" + _database + "\" WITH (FORCE)",admin).ExecuteNonQueryAsync();
    }
    private void Role(string? role)
    {
        _client.DefaultRequestHeaders.Remove("x-user-id"); _client.DefaultRequestHeaders.Remove("x-user-roles");
        if (role is null) return;
        _client.DefaultRequestHeaders.Add("x-user-id",Guid.NewGuid().ToString()); _client.DefaultRequestHeaders.Add("x-user-roles",role);
    }
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static async Task AssertStatus(HttpResponseMessage response,HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,$"Expected {expected}, got {response.StatusCode}: {text}");
    }

    [PostgresFact]
    public async Task HealthyPublicProbes_PreserveLivenessAndDoNotExposeWorkerDetails()
    {
        var live = await _client.GetAsync("/health"); await AssertStatus(live,HttpStatusCode.OK);
        var json = Json(await live.Content.ReadAsStringAsync());
        Assert.Equal("Healthy",json.GetProperty("status").GetString()); Assert.Equal(2,json.EnumerateObject().Count());
        Assert.True(live.Headers.CacheControl!.NoStore);
        var ready = await _client.GetAsync("/health/ready"); await AssertStatus(ready,HttpStatusCode.OK);
        var body = await ready.Content.ReadAsStringAsync();
        Assert.Equal("Ready",Json(body).GetProperty("status").GetString()); Assert.True(ready.Headers.CacheControl!.NoStore);
        foreach (var secret in new[] { "errorCode", "consecutiveFailures", "OtpEmail", "EventLogRetention", "password", "payload" })
            Assert.DoesNotContain(secret,body);
        await using var db = Db(); Assert.False(db.Database.HasPendingModelChanges());
        Assert.Same(Registry,_app!.Services.GetRequiredService<IWorkerHealthReader>());
        Assert.Same(Registry,_app.Services.GetRequiredService<IWorkerHealthReporter>());
    }

    [PostgresFact]
    public async Task OperatorDetail_EnforcesRolesAndShowsDisabledWorkersWithoutBlockingReadiness()
    {
        await AssertStatus(await _client.GetAsync(Detail),HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "buyer", "seller", "service" })
        {
            Role(role); await AssertStatus(await _client.GetAsync(Detail),HttpStatusCode.Forbidden);
        }
        foreach (var role in new[] { "admin", "staff" })
        {
            Role(role); var response = await _client.GetAsync(Detail); await AssertStatus(response,HttpStatusCode.OK);
            Assert.True(response.Headers.CacheControl!.NoStore);
            var json = Json(await response.Content.ReadAsStringAsync());
            Assert.Equal("Healthy",json.GetProperty("database").GetProperty("status").GetString());
            var workers = json.GetProperty("workers").EnumerateArray().ToList(); Assert.Equal(3,workers.Count);
            Assert.Equal("Disabled",workers.Single(w => w.GetProperty("name").GetString() == "EventLogRetention").GetProperty("status").GetString());
        }
    }

    [PostgresFact]
    public async Task RequiredWorkerFaultsAndStall_Return503ThenRecover()
    {
        // This case injects registry states; stop the actual reporter before simulating them.
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _app!.Services.GetServices<IHostedService>().OfType<DurableOtpWorker>().Single().StopAsync(stop.Token);
        Role("staff");
        foreach (var state in new[] { WorkerActivity.NotStarted,WorkerActivity.Failed,WorkerActivity.Stopped })
        {
            Registry.Report(WorkerName.OtpEmail,state);
            await AssertStatus(await _client.GetAsync("/health/ready"),HttpStatusCode.ServiceUnavailable);
            await AssertStatus(await _client.GetAsync(Detail),HttpStatusCode.ServiceUnavailable);
            await AssertStatus(await _client.GetAsync("/health"),HttpStatusCode.OK);
            Registry.Report(WorkerName.OtpEmail,WorkerActivity.Waiting);
            await AssertStatus(await _client.GetAsync("/health/ready"),HttpStatusCode.OK);
        }
        Registry.Report(WorkerName.OtpEmail,WorkerActivity.Running); _clock.Advance(TimeSpan.FromSeconds(121));
        await AssertStatus(await _client.GetAsync("/health/ready"),HttpStatusCode.ServiceUnavailable);
        var details = Json(await (await _client.GetAsync(Detail)).Content.ReadAsStringAsync());
        Assert.Contains(details.GetProperty("workers").EnumerateArray(),w => w.GetProperty("status").GetString() == "Stalled");
        Registry.Report(WorkerName.OtpEmail,WorkerActivity.Waiting);
        await AssertStatus(await _client.GetAsync("/health/ready"),HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task DatabasePendingMigrations_BlockReadinessWithoutChangingLiveness()
    {
        await using var db = Db();
        var last = (await db.Database.GetAppliedMigrationsAsync()).Last();
        // Only this fixture's disposable database history marker is changed.
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\"={last}");
        var probe = new DatabaseHealthProbe(db,new(),_clock);
        Assert.Equal("DATABASE_MIGRATIONS_PENDING",(await probe.CheckAsync()).ErrorCode);
        var ready = await _client.GetAsync("/health/ready"); await AssertStatus(ready,HttpStatusCode.ServiceUnavailable);
        Assert.DoesNotContain("MIGRATIONS",await ready.Content.ReadAsStringAsync());
        await AssertStatus(await _client.GetAsync("/health"),HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task CriticalSchemaAccessFailure_IsSanitizedAndRecovers()
    {
        await using var db = Db();
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "EventLogs" RENAME COLUMN "TraceId" TO "UnavailableTraceId" """);
        await AssertStatus(await _client.GetAsync("/health/ready"),HttpStatusCode.ServiceUnavailable);
        Role("admin"); var details = await _client.GetAsync(Detail); await AssertStatus(details,HttpStatusCode.ServiceUnavailable);
        var text = await details.Content.ReadAsStringAsync();
        Assert.Equal("DATABASE_SCHEMA_UNAVAILABLE",Json(text).GetProperty("database").GetProperty("errorCode").GetString());
        foreach (var value in new[] { "UnavailableTraceId", "PostgresException", "Host=", "StackTrace" }) Assert.DoesNotContain(value,text);
        await AssertStatus(await _client.GetAsync("/health"),HttpStatusCode.OK);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "EventLogs" RENAME COLUMN "UnavailableTraceId" TO "TraceId" """);
        await AssertStatus(await _client.GetAsync("/health/ready"),HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task DatabaseTimeout_CancelsBlockedRead_AndLaterProbeRecovers()
    {
        await using var locker = Db(); await using var tx = await locker.Database.BeginTransactionAsync();
        await locker.Database.ExecuteSqlRawAsync("""LOCK TABLE "EventLogs" IN ACCESS EXCLUSIVE MODE""");
        await using var db = Db();
        var probe = new DatabaseHealthProbe(db,new() { DatabaseTimeoutSeconds = 1 },_clock);
        var result = await probe.CheckAsync().WaitAsync(TimeSpan.FromSeconds(7));
        Assert.Equal("DATABASE_TIMEOUT",result.ErrorCode);
        await tx.RollbackAsync();
        Assert.True((await probe.CheckAsync()).IsHealthy);
    }

    [PostgresFact]
    public async Task DatabaseProbe_CallerCancellationIsPropagated()
    {
        await using var locker = Db(); await using var tx = await locker.Database.BeginTransactionAsync();
        await locker.Database.ExecuteSqlRawAsync("""LOCK TABLE "EventLogs" IN ACCESS EXCLUSIVE MODE""");
        await using var db = Db();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DatabaseHealthProbe(db,new(),_clock).CheckAsync(cts.Token));
        await tx.RollbackAsync();
    }

    [PostgresFact]
    public async Task UnreachableDatabase_IsUnhealthy_WithoutLeakingConnectionDetails()
    {
        await using var db = Db("Host=127.0.0.1;Port=1;Database=unused;Username=private-user;Password=private-password;Timeout=1");
        // Let the driver's 1-second connection failure complete before the 3-second probe deadline.
        var result = await new DatabaseHealthProbe(db,new() { DatabaseTimeoutSeconds = 3 },_clock).CheckAsync();
        Assert.False(result.IsHealthy); Assert.Equal("DATABASE_UNAVAILABLE",result.ErrorCode);
        Assert.DoesNotContain("private",JsonSerializer.Serialize(result));
    }

    [PostgresFact]
    public async Task RepeatedHealthReads_WriteNoHealthAuditDeliveryOrEventData()
    {
        await using var db = Db();
        var events = await db.EventLogs.IgnoreQueryFilters().CountAsync(); var audits = await db.AuditLogs.CountAsync();
        var deliveries = await db.NotificationDeliveries.CountAsync(); var history = await db.BrokerHealthChecks.CountAsync();
        Role("admin");
        for (var i = 0; i < 3; i++)
        {
            await AssertStatus(await _client.GetAsync("/health/ready"),HttpStatusCode.OK);
            await AssertStatus(await _client.GetAsync(Detail),HttpStatusCode.OK);
        }
        Assert.Equal(events,await db.EventLogs.IgnoreQueryFilters().CountAsync()); Assert.Equal(audits,await db.AuditLogs.CountAsync());
        Assert.Equal(deliveries,await db.NotificationDeliveries.CountAsync()); Assert.Equal(history,await db.BrokerHealthChecks.CountAsync());
    }

    [PostgresFact]
    public async Task Swagger_ContainsOperationalProbesAndOperatorHealthEndpoint()
    {
        var paths = Json(await _client.GetStringAsync("/swagger/v1/swagger.json")).GetProperty("paths");
        foreach (var route in new[] { "/health", "/health/ready", Detail }) Assert.True(paths.GetProperty(route).TryGetProperty("get",out _));
        Assert.True(paths.GetProperty("/health/ready").GetProperty("get").GetProperty("responses").TryGetProperty("503",out _));
        Assert.True(paths.GetProperty(Detail).GetProperty("get").GetProperty("responses").TryGetProperty("503",out _));
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-11-08T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
}
