namespace Notification.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Notification.Api.Common;
using Notification.Api.Controllers.Public;
using Notification.Api.Middleware;
using Notification.Application.Interfaces;
using Notification.Application.Services.Deliveries;
using Notification.Application.Services.Dispatches;
using Notification.Application.Services.Management;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class ManagementApiIntegrationTests : IAsyncLifetime
{
    private const string Root = "/api/v1/admin/notifications";
    private string? _admin, _database, _connection;
    private WebApplication? _app;
    private HttpClient _client = null!;
    private readonly ManagementActor _actor = new(Guid.NewGuid(), "management-test", "127.0.0.1", "Tests");
    private AppDbContext Db(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }
    private NotificationManagementService Service(AppDbContext db) => new(db, TimeProvider.System);

    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_management_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"", admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(NotificationManagementController).Assembly)
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiValidationProblemFactory.Create);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connection));
        builder.Services.AddScoped<INotificationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<INotificationManagementService, NotificationManagementService>();
        _app = builder.Build();
        _app.UseGlobalExceptionHandler();
        _app.UseMiddleware<UserContextMiddleware>();
        _app.UseSwagger();
        _app.MapControllers();
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        Role("admin");
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
        if (_database is null || _admin is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await new NpgsqlCommand("DROP DATABASE \"" + _database + "\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }

    private void Role(string? role)
    {
        _client.DefaultRequestHeaders.Remove("x-user-id");
        _client.DefaultRequestHeaders.Remove("x-user-roles");
        if (role is null) return;
        _client.DefaultRequestHeaders.Add("x-user-id", _actor.UserId.ToString());
        _client.DefaultRequestHeaders.Add("x-user-roles", role);
    }
    private static CreateTemplateRequest Template(string? code = null) => new()
    {
        Code = code ?? "TEST_" + Guid.NewGuid().ToString("N"), NotificationType = "OrderShipped", ChannelCode = "Email",
        SubjectTemplate = "Hello {{OrderNo}}", BodyTemplate = "Body {{OrderNo}}"
    };
    private static RetryDeliveryRequest Retry() => new() { RequestId = Guid.NewGuid(), ExpectedRowVersion = 2, Reason = "Provider recovered" };
    private async Task<DispatchResponse> Failed()
    {
        await using var db = Db();
        var result = await new DispatchService(db).CreateAsync(DispatchServiceTests.Request("ORDER_PAID_EMAIL"));
        await db.NotificationDeliveries.Where(d => d.Id == result.DeliveryId).ExecuteUpdateAsync(s => s
            .SetProperty(d => d.Status, DeliveryStatus.Failed).SetProperty(d => d.RowVersion, 2L)
            .SetProperty(d => d.AttemptCount, 3).SetProperty(d => d.ErrorCode, "SMTP_450").SetProperty(d => d.ErrorMessage, "Private error"));
        await db.Notifications.Where(n => n.Id == result.NotificationId).ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, NotificationStatus.Failed));
        return result;
    }
    private static async Task Code(HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {body}");
        if (code is not null) Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("errorCode").GetString());
    }

    [PostgresFact]
    public async Task Authorization_StaffReadsButCannotMutate_Anonymous401AndBuyer403()
    {
        Role(null); await Code(await _client.GetAsync(Root + "/templates"), HttpStatusCode.Unauthorized);
        Role("buyer"); await Code(await _client.GetAsync(Root + "/channels"), HttpStatusCode.Forbidden);
        Role("staff");
        foreach (var path in new[] { "/templates", "/channels", "/deliveries" })
            await Code(await _client.GetAsync(Root + path), HttpStatusCode.OK);
        await Code(await _client.PostAsJsonAsync(Root + "/templates", Template()), HttpStatusCode.Forbidden);
        await Code(await _client.PutAsJsonAsync(Root + "/templates/" + Guid.NewGuid(), new { }), HttpStatusCode.Forbidden);
        await Code(await _client.PutAsJsonAsync(Root + "/channels/Email", new { }), HttpStatusCode.Forbidden);
        await Code(await _client.PostAsJsonAsync(Root + "/deliveries/" + Guid.NewGuid() + "/retries", Retry()), HttpStatusCode.Forbidden);
        await using var db = Db(); Assert.Empty(await db.AuditLogs.Where(a => a.UserId == _actor.UserId.ToString()).ToListAsync());
    }

    [PostgresFact]
    public async Task Template_CreateUpdate_RequiresFreshVersion_AndAudits()
    {
        var request = Template();
        var created = await _client.PostAsJsonAsync(Root + "/templates", request);
        await Code(created, HttpStatusCode.Created);
        var row = (await created.Content.ReadFromJsonAsync<TemplateResponse>())!;
        Assert.Equal("OrderShipped", row.NotificationType); Assert.Equal("Email", row.ChannelCode);
        await Code(await _client.GetAsync(Root + "/templates/" + row.Id), HttpStatusCode.OK);
        var update = new UpdateTemplateRequest { ExpectedUpdatedAt = row.UpdatedAt, SubjectTemplate = "Updated", BodyTemplate = "New body", IsMandatory = true, IsActive = false };
        var updated = await _client.PutAsJsonAsync(Root + "/templates/" + row.Id, update);
        await Code(updated, HttpStatusCode.OK);
        Assert.True((await updated.Content.ReadFromJsonAsync<TemplateResponse>())!.UpdatedAt > row.UpdatedAt);
        await Code(await _client.PutAsJsonAsync(Root + "/templates/" + row.Id, update), HttpStatusCode.Conflict, "CONFIG_VERSION_CONFLICT");
        await Code(await _client.PostAsJsonAsync(Root + "/templates", request), HttpStatusCode.Conflict, "TEMPLATE_CONFLICT");
        await using var db = Db();
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.EntityId == row.Id.ToString()));
        Assert.False(db.Database.HasPendingModelChanges());
        await db.NotificationTemplates.Where(t => t.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true));
        await Code(await _client.PostAsJsonAsync(Root + "/templates", request), HttpStatusCode.Conflict, "TEMPLATE_CONFLICT");
    }

    [PostgresFact]
    public async Task Validation_RejectsMalformedTemplatesEnumsCriticalOptionalAndPagination()
    {
        await Code(await _client.PostAsJsonAsync(Root + "/templates", Template() with { SubjectTemplate = "{{broken" }), HttpStatusCode.BadRequest, "VAL_INVALID_TEMPLATE");
        await Code(await _client.PostAsJsonAsync(Root + "/templates", Template() with { NotificationType = "OrderPaid" }), HttpStatusCode.BadRequest, "MANDATORY_TEMPLATE_REQUIRED");
        await Code(await _client.PostAsJsonAsync(Root + "/templates", Template() with { ChannelCode = "0" }), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT");
        foreach (var filter in new[] { "templates?page=0", "templates?pageSize=101", "templates?page=2147483647&pageSize=100", "templates?channel=0", "templates?notificationType=Email,Push", "deliveries?status=99", "deliveries?userId=" + Guid.Empty, "deliveries?from=2026-12-01T00:00:00Z&to=2026-01-01T00:00:00Z" })
            await Code(await _client.GetAsync(Root + "/" + filter), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT");
        await Code(await _client.GetAsync(Root + "/templates/" + Guid.NewGuid()), HttpStatusCode.NotFound, "TEMPLATE_NOT_FOUND");
        await Code(await _client.GetAsync(Root + "/deliveries/" + Guid.NewGuid()), HttpStatusCode.NotFound, "DELIVERY_NOT_FOUND");
    }

    [PostgresFact]
    public async Task ChannelUpdate_DoesNotExposeOrOverwriteCredentials_AndRejectsStaleWrites()
    {
        await using var db = Db();
        await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.Config, "{\"password\":\"secret-value\"}"));
        var channel = await db.NotificationChannels.AsNoTracking().SingleAsync(c => c.Code == ChannelType.Email);
        var list = await _client.GetStringAsync(Root + "/channels");
        Assert.DoesNotContain("secret-value", list); Assert.DoesNotContain("config", list, StringComparison.OrdinalIgnoreCase);
        var request = new UpdateChannelRequest { Name = "Mail channel", IsDisabled = true, ExpectedUpdatedAt = channel.UpdatedAt };
        await Code(await _client.PutAsJsonAsync(Root + "/channels/Email", request), HttpStatusCode.OK);
        await Code(await _client.PutAsJsonAsync(Root + "/channels/Email", request), HttpStatusCode.Conflict, "CONFIG_VERSION_CONFLICT");
        Assert.Equal(channel.Config, (await db.NotificationChannels.AsNoTracking().SingleAsync(c => c.Id == channel.Id)).Config);
        Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditAction.ChannelUpdate).ToListAsync());
    }

    [PostgresFact]
    public async Task History_PaginatesFiltersAndMasksLegacySecrets_AlsoForHiddenParents()
    {
        var delivery = await Failed();
        await using var db = Db();
        db.NotificationLogs.Add(new NotificationLog { Id = Guid.NewGuid(), NotificationId = delivery.NotificationId, DeliveryId = delivery.DeliveryId,
            Action = LogAction.Failed, Level = Notification.Domain.Enums.LogLevel.Error, Message = "secret-token-123",
            Metadata = "{\"Token\":\"secret-token-123\",\"Attempt\":3,\"Details\":{\"ErrorCode\":\"SMTP_450\",\"ErrorMessage\":\"secret-token-123\"}}" });
        await db.SaveChangesAsync();
        await db.Notifications.Where(n => n.Id == delivery.NotificationId).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDeleted, true));
        var detail = await _client.GetStringAsync(Root + "/deliveries/" + delivery.DeliveryId);
        Assert.Contains("***@example.com", detail); Assert.DoesNotContain("user@example.com", detail); Assert.DoesNotContain("Private error", detail);
        var logs = await _client.GetStringAsync(Root + "/deliveries/" + delivery.DeliveryId + "/logs");
        Assert.Contains("SMTP_450", logs); Assert.DoesNotContain("secret-token-123", logs);
        var list = await _client.GetStringAsync(Root + "/deliveries?status=Failed&channel=Email&pageSize=1&notificationId=" + delivery.NotificationId);
        using var json = JsonDocument.Parse(list);
        Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        var next = await _client.GetStringAsync(Root + "/deliveries?page=2&pageSize=1&notificationId=" + delivery.NotificationId);
        Assert.Empty(JsonDocument.Parse(next).RootElement.GetProperty("items").EnumerateArray());
    }

    [PostgresFact]
    public async Task Swagger_ListsTenOperationsAndPaginationFilters()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = json.RootElement.GetProperty("paths").EnumerateObject()
            .Where(p => p.Value.EnumerateObject().Any(operation => operation.Value.TryGetProperty("tags", out var tags) &&
                tags.EnumerateArray().Any(tag => tag.GetString() == "NotificationManagement"))).ToList();
        Assert.Equal(10, paths.Sum(p => p.Value.EnumerateObject().Count()));
        var parameters = json.RootElement.GetProperty("paths").GetProperty(Root + "/deliveries").GetProperty("get").GetProperty("parameters");
        Assert.Contains(parameters.EnumerateArray(), p => p.GetProperty("name").GetString() == "PageSize");
    }

    [PostgresFact]
    public async Task Retry_HTTP202_ResetsBudget_LogsAndAuditsOnce_ReplayDoesNotRequeueSent()
    {
        var delivery = await Failed(); var request = Retry();
        var sender = new Mock<IEmailDeliverySender>();
        sender.Setup(s => s.SendAsync(It.IsAny<EmailDeliveryMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(EmailDeliveryResult.Sent());
        await using (var db = Db()) Assert.False(await new DeliveryService(db, sender.Object, new(), TimeProvider.System).ProcessNextAsync());
        var response = await _client.PostAsJsonAsync(Root + "/deliveries/" + delivery.DeliveryId + "/retries", request);
        await Code(response, HttpStatusCode.Accepted);
        var accepted = (await response.Content.ReadFromJsonAsync<RetryAcceptanceResponse>())!;
        Assert.False(accepted.IsReplay); Assert.Equal(3, accepted.AcceptedRowVersion);
        await using (var db = Db())
        {
            var row = await db.NotificationDeliveries.SingleAsync(d => d.Id == delivery.DeliveryId);
            Assert.Equal(DeliveryStatus.Pending, row.Status); Assert.Equal(0, row.AttemptCount); Assert.Null(row.ErrorCode); Assert.Null(row.ErrorMessage);
            Assert.Equal(NotificationStatus.Pending, (await db.Notifications.SingleAsync(n => n.Id == delivery.NotificationId)).Status);
            Assert.Equal(3, (await db.DeliveryRetryRequests.SingleAsync()).PreviousAttemptCount);
        }
        // Production workers resolve a fresh DbContext per attempt, separate from API/read scopes.
        await using (var workerDb = Db()) Assert.True(await new DeliveryService(workerDb, sender.Object, new(), TimeProvider.System).ProcessNextAsync());
        var replay = await _client.PostAsJsonAsync(Root + "/deliveries/" + delivery.DeliveryId + "/retries", request);
        await Code(replay, HttpStatusCode.Accepted);
        Assert.Equal(accepted with { IsReplay = true }, await replay.Content.ReadFromJsonAsync<RetryAcceptanceResponse>());
        await using var verify = Db();
        Assert.Equal(DeliveryStatus.Sent, (await verify.NotificationDeliveries.SingleAsync(d => d.Id == delivery.DeliveryId)).Status);
        Assert.Single(await verify.DeliveryRetryRequests.ToListAsync());
        Assert.Single(await verify.AuditLogs.Where(a => a.Action == AuditAction.DeliveryRetry).ToListAsync());
        Assert.Single(await verify.NotificationLogs.Where(l => l.Action == LogAction.Retry).ToListAsync());
        sender.Verify(s => s.SendAsync(It.IsAny<EmailDeliveryMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        await Code(await _client.PostAsJsonAsync(Root + "/deliveries/" + delivery.DeliveryId + "/retries", request with { Reason = "Other reason" }), HttpStatusCode.Conflict, "RETRY_REQUEST_CONFLICT");
    }

    [PostgresFact]
    public async Task Retry_ConcurrentSameKeyCreatesOneAcceptance_AndDifferentKeysRespectVersion()
    {
        var delivery = await Failed(); var request = Retry();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => { await using var db = Db(); return await Service(db).RetryAsync(delivery.DeliveryId, request, _actor); })).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results, r => !r.IsReplay);
        await using (var db = Db()) Assert.Single(await db.DeliveryRetryRequests.ToListAsync());
        var other = await Failed();
        var statuses = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = Db();
            try { await Service(db).RetryAsync(other.DeliveryId, Retry(), _actor); return 202; }
            catch (DomainException ex) { Assert.Equal("DELIVERY_VERSION_CONFLICT", ex.ErrorCode); return ex.StatusCode; }
        }));
        Assert.Single(statuses, s => s == 202); Assert.Equal(3, statuses.Count(s => s == 409));
    }

    [PostgresFact]
    public async Task Retry_SameKeyAcrossDifferentDeliveries_RollsBackLosingRequeue()
    {
        var first = await Failed(); var second = await Failed(); var request = Retry(); var gate = new SaveBarrier();
        var statuses = await Task.WhenAll(new[] { first, second }.Select(async delivery =>
        {
            await using var db = Db(gate);
            try { await Service(db).RetryAsync(delivery.DeliveryId, request, _actor); return 202; }
            catch (DomainException ex) { Assert.Equal("RETRY_REQUEST_CONFLICT", ex.ErrorCode); return ex.StatusCode; }
        })).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(statuses, s => s == 202); Assert.Single(statuses, s => s == 409);
        await using var verify = Db();
        Assert.Single(await verify.DeliveryRetryRequests.ToListAsync());
        Assert.Single(await verify.NotificationDeliveries.Where(d => d.Status == DeliveryStatus.Pending && d.RowVersion == 3).ToListAsync());
        Assert.Single(await verify.NotificationDeliveries.Where(d => d.Status == DeliveryStatus.Failed && d.RowVersion == 2 && d.AttemptCount == 3).ToListAsync());
        Assert.Single(await verify.AuditLogs.Where(a => a.Action == AuditAction.DeliveryRetry).ToListAsync());
    }

    [PostgresFact]
    public async Task Retry_RejectsAllNonFailedStatesDisabledChannelAndHiddenParent()
    {
        var delivery = await Failed();
        foreach (var state in new[] { DeliveryStatus.Pending, DeliveryStatus.Sending, DeliveryStatus.Sent, DeliveryStatus.Skipped })
        {
            await using var db = Db();
            await db.NotificationDeliveries.Where(d => d.Id == delivery.DeliveryId).ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, state));
            var ex = await Assert.ThrowsAsync<DomainException>(() => Service(db).RetryAsync(delivery.DeliveryId, Retry(), _actor));
            Assert.Equal("DELIVERY_NOT_RETRYABLE", ex.ErrorCode);
        }
        await using (var db = Db())
        {
            await db.NotificationDeliveries.Where(d => d.Id == delivery.DeliveryId).ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DeliveryStatus.Failed));
            await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled, true));
            Assert.Equal("CHANNEL_DISABLED", (await Assert.ThrowsAsync<DomainException>(() => Service(db).RetryAsync(delivery.DeliveryId, Retry(), _actor))).ErrorCode);
            await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled, false));
            await db.Notifications.Where(n => n.Id == delivery.NotificationId).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDeleted, true));
            Assert.Equal("DELIVERY_NOT_RETRYABLE", (await Assert.ThrowsAsync<DomainException>(() => Service(db).RetryAsync(delivery.DeliveryId, Retry(), _actor))).ErrorCode);
        }
    }

    [PostgresFact]
    public async Task Retry_InvalidPushDeviceOrNonTemplateDelivery_IsRejected()
    {
        var delivery = await Failed();
        await using var db = Db();
        await db.NotificationDeliveries.Where(d => d.Id == delivery.DeliveryId).ExecuteUpdateAsync(s => s.SetProperty(d => d.ChannelCode, ChannelType.Push).SetProperty(d => d.Destination, Guid.NewGuid().ToString()));
        Assert.Equal("DEVICE_TOKEN_INACTIVE", (await Assert.ThrowsAsync<DomainException>(() => Service(db).RetryAsync(delivery.DeliveryId, Retry(), _actor))).ErrorCode);
        await db.Notifications.Where(n => n.Id == delivery.NotificationId).ExecuteUpdateAsync(s => s.SetProperty(n => n.TemplateId, (Guid?)null));
        Assert.Equal("DELIVERY_NOT_RETRYABLE", (await Assert.ThrowsAsync<DomainException>(() => Service(db).RetryAsync(delivery.DeliveryId, Retry(), _actor))).ErrorCode);
    }

    [PostgresFact]
    public async Task Retry_SaveFailure_RollsBackDeliveryParentRequestLogAndAudit()
    {
        var delivery = await Failed();
        await using (var db = Db(new FailSave())) await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).RetryAsync(delivery.DeliveryId, Retry(), _actor));
        await using var verify = Db();
        var row = await verify.NotificationDeliveries.SingleAsync(d => d.Id == delivery.DeliveryId);
        Assert.Equal(DeliveryStatus.Failed, row.Status); Assert.Equal(2, row.RowVersion); Assert.Equal(3, row.AttemptCount);
        Assert.Equal(NotificationStatus.Failed, (await verify.Notifications.SingleAsync(n => n.Id == delivery.NotificationId)).Status);
        Assert.Empty(await verify.DeliveryRetryRequests.ToListAsync()); Assert.Empty(await verify.AuditLogs.Where(a => a.Action == AuditAction.DeliveryRetry).ToListAsync());
        Assert.Empty(await verify.NotificationLogs.Where(l => l.Action == LogAction.Retry).ToListAsync());
    }

    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private int _count;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _count) == 2) _release.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }
    private sealed class FailSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated persistence failure.");
    }
}
