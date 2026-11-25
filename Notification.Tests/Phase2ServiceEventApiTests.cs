namespace Notification.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Notification.Api.Common;
using Notification.Api.Controllers.Public;
using Notification.Api.Middleware;
using Notification.Application;
using Notification.Application.Interfaces;
using Notification.Application.Services.Notifications;
using Notification.Application.Services.ServiceEvents;
using Notification.Application.Services.WalletEvents;
using Notification.Domain.Enums;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class Phase2ServiceEventApiTests : IAsyncLifetime
{
    private const string Key = "phase2-wallet-notification-test-key-0123456789";
    private string? _admin, _database, _connection;
    private WebApplication? _app;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "phase2_notification_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db())
        {
            await db.Database.MigrateAsync();
            // This database was created above for this test only. Keep configuration/template seeds,
            // remove demonstration notifications and logs so assertions concern actual HTTP events.
            await db.NotificationLogs.ExecuteDeleteAsync();
            await db.NotificationDeliveries.ExecuteDeleteAsync();
            await db.Notifications.ExecuteDeleteAsync();
            await db.AuditLogs.ExecuteDeleteAsync();
        }
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration["WalletNotificationAuth:ServiceKey"] = Key;
        builder.Configuration["InternalSecurity:ServiceKey"] = Key;
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(WithdrawalEventsController).Assembly)
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiValidationProblemFactory.Create);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options => options.OperationFilter<Notification.Api.Swagger.ServiceEventOperationFilter>());
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connection));
        builder.Services.AddScoped<INotificationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddApplicationServices();
        AuctionServiceTestToken.Configure(builder.Services, builder.Configuration);
        _app = builder.Build();
        _app.UseGlobalExceptionHandler();
        _app.UseMiddleware<UserContextMiddleware>();
        _app.UseMiddleware<InternalServiceAuthMiddleware>();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseSwagger();
        _app.MapControllers();
        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        _http.DefaultRequestHeaders.Add("X-Internal-Service-Key", Key);
    }

    public async Task DisposeAsync()
    {
        _http?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
        if (_database is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE \"{_database}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }

    [PostgresTheory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    [InlineData("Completed")]
    [InlineData("Failed")]
    public async Task Withdrawal_ActualCallerShapePersistsPrivateInApp(string status)
    {
        var r = Withdrawal(status);
        var response = await _http.PostAsJsonAsync(Route("withdrawal-events"), r);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ServiceEventResponse>())!;
        await using var db = Db();
        var n = await db.Notifications.SingleAsync();
        Assert.Equal(result.NotificationId, n.Id);
        Assert.Equal(r.RecipientUserId, n.UserId);
        Assert.Equal(NotificationType.WithdrawalResult, n.NotificationType);
        Assert.Contains(status, n.Payload!);
        Assert.Contains(r.DisplayReason!, n.Body!);
        Assert.Single(await db.NotificationDeliveries.ToListAsync());
        Assert.Equal(ChannelType.InApp, (await db.NotificationDeliveries.SingleAsync()).ChannelCode);
        Assert.Equal(AuditAction.ServiceEventAccepted, (await db.AuditLogs.SingleAsync()).Action);
    }

    [PostgresTheory]
    [InlineData("Held")]
    [InlineData("Released")]
    [InlineData("Captured")]
    [InlineData("Forfeited")]
    [InlineData("Refunded")]
    public async Task WalletDeposit_AllConfirmedStatesUseSameRouteWithoutAuctionTitle(string status)
    {
        var r = Deposit(status);
        var response = await _http.PostAsJsonAsync(Route("auction-deposits"), r);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = Db();
        var n = await db.Notifications.SingleAsync();
        Assert.Equal(r.BidderId, n.UserId);
        Assert.Equal(NotificationType.AuctionDeposit, n.NotificationType);
        Assert.Contains(status, n.Payload!);
    }

    [PostgresFact]
    public async Task ConcurrentReplayConflictAndDeletedReceiptAreStable()
    {
        var r = Withdrawal("Completed");
        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => _http.PostAsJsonAsync(Route("withdrawal-events"), r)));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        var replay = await _http.PostAsJsonAsync(Route("withdrawal-events"), r with
            { TraceId = "retry", ConfirmedAt = r.ConfirmedAt.ToOffset(TimeSpan.FromHours(7)) });
        Assert.True((await replay.Content.ReadFromJsonAsync<ServiceEventResponse>())!.IsReplay);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsJsonAsync(Route("withdrawal-events"), r with
            { RecipientUserId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsJsonAsync(Route("withdrawal-events"), r with
            { Status = "Failed" })).StatusCode);
        await using var db = Db();
        var row = await db.Notifications.SingleAsync();
        row.IsDeleted = true;
        await db.SaveChangesAsync();
        var afterDelete = await _http.PostAsJsonAsync(Route("withdrawal-events"), r);
        Assert.True((await afterDelete.Content.ReadFromJsonAsync<ServiceEventResponse>())!.IsReplay);
        Assert.Single(await db.Notifications.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.NotificationDeliveries.ToListAsync());
        Assert.Single(await db.AuditLogs.ToListAsync());
    }

    [PostgresFact]
    public async Task DisabledInAppPreservesReceiptWithoutQueueingPushOrEmail()
    {
        await using var db = Db();
        (await db.NotificationChannels.SingleAsync(c => c.Code == ChannelType.InApp)).IsDisabled = true;
        await db.SaveChangesAsync();
        var r = Deposit("Refunded");
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await _http.PostAsJsonAsync(Route("auction-deposits"), r)).StatusCode);
        Assert.False((await db.Notifications.AsNoTracking().SingleAsync()).IsInboxVisible);
        Assert.Equal(DeliveryStatus.Skipped, (await db.NotificationDeliveries.SingleAsync()).Status);
    }

    [PostgresFact]
    public async Task WalletAuthenticationDoesNotAcceptAnonymousForgedOrAuctionIdentity()
    {
        foreach (var endpoint in new[] { "withdrawal-events", "auction-deposits" })
        {
            _http.DefaultRequestHeaders.Remove("X-Internal-Service-Key");
            Assert.Equal(HttpStatusCode.Unauthorized, (await _http.PostAsJsonAsync(Route(endpoint), Withdrawal("Approved"))).StatusCode);
            _http.DefaultRequestHeaders.Add("X-Internal-Service-Key", "wrong");
            Assert.Equal(HttpStatusCode.Unauthorized, (await _http.PostAsJsonAsync(Route(endpoint), Withdrawal("Approved"))).StatusCode);
            _http.DefaultRequestHeaders.Remove("X-Internal-Service-Key");
            _http.DefaultRequestHeaders.Add("X-Internal-Service-Key", Key);
            _http.DefaultRequestHeaders.Add("x-user-id", Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.Unauthorized, (await _http.PostAsJsonAsync(Route(endpoint), Withdrawal("Approved"))).StatusCode);
            _http.DefaultRequestHeaders.Remove("x-user-id");
        }
        _http.DefaultRequestHeaders.Remove("X-Internal-Service-Key");
        _http.DefaultRequestHeaders.Authorization = AuctionServiceTestToken.Create();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.PostAsJsonAsync(Route("withdrawal-events"), Withdrawal("Approved"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync(Route("auction-deposits"), Deposit("Held"))).StatusCode);
    }

    [PostgresFact]
    public async Task InvalidFieldsAndMixedContractsAreRejected()
    {
        var wd = Withdrawal("Approved");
        foreach (var r in new[] { wd with { Status = "Processing" }, wd with { RecipientUserId = Guid.Empty },
            wd with { SourceEventId = "" }, wd with { ConfirmedAt = default }, wd with { DisplayReason = new string('x', 1001) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync(Route("withdrawal-events"), r)).StatusCode);
        var dep = Deposit("Held");
        foreach (var r in new[] { dep with { Status = "Pending" }, dep with { Amount = -1 }, dep with { Currency = "USD" },
            dep with { Amount = 0.001m }, dep with { BidderId = Guid.Empty }, dep with { ConfirmedAt = default } })
            Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync(Route("auction-deposits"), r)).StatusCode);
        var json = JsonSerializer.SerializeToNode(dep, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        json["auctionTitle"] = "Unexpected Auction field";
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync(Route("auction-deposits"), json)).StatusCode);
        await using var db = Db();
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [PostgresFact]
    public async Task InboxOwnershipAndReadStateAreEnforcedForWalletNotices()
    {
        var r = Withdrawal("Completed");
        var res = await _http.PostAsJsonAsync(Route("withdrawal-events"), r);
        var id = (await res.Content.ReadFromJsonAsync<ServiceEventResponse>())!.NotificationId;
        _http.DefaultRequestHeaders.Remove("X-Internal-Service-Key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync(Route(id.ToString()))).StatusCode);
        _http.DefaultRequestHeaders.Add("x-user-id", Guid.NewGuid().ToString());
        _http.DefaultRequestHeaders.Add("x-user-roles", "buyer");
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(Route(id.ToString()))).StatusCode);
        _http.DefaultRequestHeaders.Remove("x-user-id");
        _http.DefaultRequestHeaders.Add("x-user-id", r.RecipientUserId.ToString());
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync(Route(id.ToString()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.PutAsJsonAsync(Route($"{id}/read-states"), new { isRead = true })).StatusCode);
        await using var db = Db();
        Assert.True((await db.Notifications.SingleAsync()).IsRead);
    }

    [PostgresTheory]
    [InlineData("PRODUCT_BANNED_REPORT", "ProductBannedByReport")]
    [InlineData("ProductBannedByReport", "ProductBannedByReport")]
    [InlineData("REPORT_RESOLVED", "ProductReportResolved")]
    [InlineData("REPORT_DISMISSED", "ProductReportDismissed")]
    public async Task CatalogLegacyTypesBecomeCanonicalPrivateReplaySafeNotices(string alias, string expected)
    {
        var request = new CreateInAppNotificationRequest(Guid.NewGuid(), "Source title", "Trừ 10 điểm SECRET-NOTE", alias,
            JsonSerializer.Serialize(new { productId = Guid.NewGuid(), reportId = Guid.NewGuid(), penaltyPoints = 10 }));
        var legacy = "/api/v1/internal/notifications/in-app";
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await _http.PostAsJsonAsync(legacy, request)).StatusCode);
        await using var db = Db();
        var n = await db.Notifications.SingleAsync();
        Assert.Equal(expected, n.NotificationType.ToString());
        Assert.DoesNotContain("10", n.Body!);
        Assert.DoesNotContain("SECRET-NOTE", n.Body!);
        Assert.DoesNotContain("penaltyPoints", n.Payload!);
        Assert.Equal(request.UserId, n.UserId);
        Assert.Single(await db.NotificationDeliveries.ToListAsync());
    }

    [PostgresFact]
    public async Task PublicCatalogIntakeRequiresServiceRoleAndValidBusinessIds()
    {
        _http.DefaultRequestHeaders.Remove("X-Internal-Service-Key");
        var r = new CreateInAppNotificationRequest(Guid.NewGuid(), "Title", "Body", "PRODUCT_BANNED_REPORT",
            JsonSerializer.Serialize(new { productId = Guid.NewGuid(), reportId = Guid.NewGuid() }), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.PostAsJsonAsync(Route("in-app"), r)).StatusCode);
        _http.DefaultRequestHeaders.Add("x-user-id", Guid.NewGuid().ToString());
        _http.DefaultRequestHeaders.Add("x-user-roles", "buyer");
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsJsonAsync(Route("in-app"), r)).StatusCode);
        _http.DefaultRequestHeaders.Remove("x-user-roles");
        _http.DefaultRequestHeaders.Add("x-user-roles", "service");
        Assert.Equal(HttpStatusCode.OK, (await _http.PostAsJsonAsync(Route("in-app"), r)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsJsonAsync(Route("in-app"), r with
            { Payload = JsonSerializer.Serialize(new { productId = Guid.NewGuid(), reportId = Guid.NewGuid() }) })).StatusCode);
        foreach (var payload in new[] { "{}", "[]", "not-json", "{\"productId\":123}" })
            Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync(Route("in-app"), r with { Payload = payload })).StatusCode);
    }

    [PostgresFact]
    public async Task SwaggerExposesOneDepositOperationAndWithdrawalAndPublicInApp()
    {
        var response = await _http.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var route in new[] { "withdrawal-events", "auction-deposits", "in-app" })
            Assert.True(json.RootElement.GetProperty("paths").GetProperty(Route(route)).TryGetProperty("post", out _));
    }

    [PostgresFact]
    public async Task MissingOrShortWalletKeyFailsClosed()
    {
        foreach (var value in new[] { "", "short" })
        {
            _app!.Configuration["WalletNotificationAuth:ServiceKey"] = value;
            Assert.Equal(HttpStatusCode.Unauthorized, (await _http.PostAsJsonAsync(Route("withdrawal-events"), Withdrawal("Approved"))).StatusCode);
        }
        await using var db = Db();
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [PostgresFact]
    public async Task AuditFailureRollsBackNoticeAndDeliveryAndAllowsRetry()
    {
        await using var db = Db();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_phase2_audit() RETURNS trigger AS $$
            BEGIN RAISE EXCEPTION 'simulated audit failure'; END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER reject_phase2_audit BEFORE INSERT ON "AuditLogs"
            FOR EACH ROW EXECUTE FUNCTION reject_phase2_audit();
            """);
        var r = Withdrawal("Completed");
        Assert.Equal(HttpStatusCode.InternalServerError, (await _http.PostAsJsonAsync(Route("withdrawal-events"), r)).StatusCode);
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Empty(await db.NotificationDeliveries.ToListAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_phase2_audit ON \"AuditLogs\"");
        Assert.Equal(HttpStatusCode.OK, (await _http.PostAsJsonAsync(Route("withdrawal-events"), r)).StatusCode);
        Assert.Single(await db.Notifications.ToListAsync());
        Assert.Single(await db.NotificationDeliveries.ToListAsync());
    }
    [PostgresTheory]
    [InlineData("PRODUCT_APPROVED", "ProductApproved")]
    [InlineData("PRODUCT_REJECTED", "ProductRejected")]
    [InlineData("PRODUCT_SUSPENDED", "ProductSuspended")]
    [InlineData("PRODUCT_BANNED", "ProductBanned")]
    public async Task CatalogStaffModerationUsesStableEventAndCanonicalType(string alias, string canonical)
    {
        _http.DefaultRequestHeaders.Remove("X-Internal-Service-Key");
        _http.DefaultRequestHeaders.Add("x-user-id", Guid.NewGuid().ToString());
        _http.DefaultRequestHeaders.Add("x-user-roles", "staff");
        var request = new CreateInAppNotificationRequest(Guid.NewGuid(), "Source", "Wrong penalty 10", alias,
            JsonSerializer.Serialize(new { productId = Guid.NewGuid(), penaltyPoints = 10 }), Guid.NewGuid());
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await _http.PostAsJsonAsync(Route("in-app"), request)).StatusCode);
        await using var db = Db();
        var notice = await db.Notifications.SingleAsync();
        Assert.Equal(canonical, notice.NotificationType.ToString());
        Assert.DoesNotContain("10", notice.Body!);
        Assert.DoesNotContain("penaltyPoints", notice.Payload!);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _http.PostAsJsonAsync(Route("in-app"), request with { SourceEventId = null })).StatusCode);
    }
    [PostgresFact]
    public async Task UpdatedWithdrawalContractReusesExistingEventIdentity()
    {
        var request = Withdrawal("Completed");
        var response = await _http.PostAsJsonAsync(Route("withdrawal-events"), request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var original = (await response.Content.ReadFromJsonAsync<ServiceEventResponse>())!;
        await using var scope = _app!.Services.CreateAsyncScope();
        var adapter = scope.ServiceProvider.GetRequiredService<Notification.Application.Services.WithdrawalEvents.IWithdrawalEventService>();
        var replay = await adapter.CreateAsync(new Notification.Application.Services.WithdrawalEvents.WithdrawalEventRequest(
            request.SourceEventId, request.WithdrawalId, request.RecipientUserId, request.Status,
            request.ConfirmedAt, request.DisplayReason, request.TransactionId, "new-trace"), default);
        Assert.True(replay.IsReplay);
        Assert.Equal(original.NotificationId, replay.NotificationId);
        await using var db = Db();
        Assert.Single(await db.Notifications.ToListAsync());
        Assert.Single(await db.NotificationDeliveries.ToListAsync());
    }
    private static WithdrawalEventRequest Withdrawal(string status) => new($"withdrawal:{Guid.NewGuid():D}:{status}",
        Guid.NewGuid(), Guid.NewGuid(), status, DateTimeOffset.UtcNow, "Lý do được phép hiển thị", "BANK-TEST", "phase2");
    private static WalletDepositEventRequest Deposit(string status) => new($"hold:{Guid.NewGuid():D}:{status}",
        Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid(), status, 500000, "VND", DateTimeOffset.UtcNow, "phase2");
    private AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection).Options);
    private static string Route(string endpoint) => "/api/v1/notifications/" + endpoint;
}

internal sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB")))
            Skip = "Set NOTIFICATION_TEST_DB to an isolated PostgreSQL test instance.";
    }
}
