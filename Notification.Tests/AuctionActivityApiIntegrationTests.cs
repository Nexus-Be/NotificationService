namespace Notification.Tests;

using System.Net;
using System.Net.Http.Json;
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
using Notification.Application.Services.AuctionActivity;
using Notification.Domain.Enums;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class AuctionActivityApiIntegrationTests : IAsyncLifetime
{
    private string? _admin, _database, _connection;
    private WebApplication? _app;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "auction_activity_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(AuctionActivityController).Assembly)
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiValidationProblemFactory.Create);
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connection));
        builder.Services.AddScoped<INotificationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddApplicationServices();
        AuctionServiceTestToken.Configure(builder.Services, builder.Configuration);
        _app = builder.Build();
        _app.UseGlobalExceptionHandler();
        _app.UseMiddleware<UserContextMiddleware>();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        _http = new HttpClient
        {
            BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };
        _http.DefaultRequestHeaders.Authorization = AuctionServiceTestToken.Create();
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


    [PostgresFact]
    public async Task ReserveApproval_IsPrivateIdempotentAndKeepsOriginalDeadline()
    {
        var seller = Guid.NewGuid();
        var request = new AuctionReserveApprovalNeededRequest(Guid.NewGuid(), Guid.NewGuid(), seller,
            "Reserve probe", 14000000, 15000000, "VND", DateTimeOffset.UtcNow.AddHours(12), "first");
        var response = await _http.PostAsJsonAsync(Route("auction-reserve-approval-needed"), request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var first = (await response.Content.ReadFromJsonAsync<AuctionActivityResponse>())!;
        var retry = await _http.PostAsJsonAsync(Route("auction-reserve-approval-needed"), request with
        {
            ApprovalDeadlineAt = request.ApprovalDeadlineAt.ToOffset(TimeSpan.FromHours(7)),
            TraceId = "retry"
        });
        var replay = (await retry.Content.ReadFromJsonAsync<AuctionActivityResponse>())!;
        Assert.True(replay.IsReplay);
        Assert.Equal(first.NotificationIds, replay.NotificationIds);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _http.PostAsJsonAsync(Route("auction-reserve-approval-needed"),
                request with { HighestBidAmount = 13000000 })).StatusCode);
        await using var db = Db();
        var row = await db.Notifications.SingleAsync(n => n.SourceEventId == request.SourceEventId);
        Assert.Equal(seller, row.UserId);
        Assert.Equal(NotificationType.AuctionReserveApprovalNeeded, row.NotificationType);
        Assert.Contains("14.000.000", row.Body);
        using var payload = System.Text.Json.JsonDocument.Parse(row.Payload!);
        Assert.Equal(request.ApprovalDeadlineAt,
            payload.RootElement.GetProperty("details").GetProperty("approvalDeadlineAt").GetDateTimeOffset());
        Assert.Single(await db.NotificationDeliveries.Where(d => d.NotificationId == row.Id).ToListAsync());
    }

    [PostgresFact]
    public async Task ReserveRestriction_WarningAndLockUseDistinctEventsForSameSeller()
    {
        var seller = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var warning = new AuctionReserveRestrictionRequest(Guid.NewGuid(), seller, 2, false, null, now);
        var locked = warning with
        {
            SourceEventId = Guid.NewGuid(), ConsecutiveDeclineCount = 3, IsRestricted = true,
            OccurredAt = now.AddSeconds(1), RestrictedUntil = now.AddDays(14)
        };
        foreach (var request in new[] { warning, locked })
        {
            var response = await _http.PostAsJsonAsync(Route("auction-reserve-restriction"), request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await _http.PostAsJsonAsync(Route("auction-reserve-restriction"), request)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Conflict,
            (await _http.PostAsJsonAsync(Route("auction-reserve-restriction"),
                locked with { SourceEventId = warning.SourceEventId })).StatusCode);
        await using var db = Db();
        var rows = await db.Notifications.Where(n => n.UserId == seller).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, n => n.Title == "Cảnh báo quyền đặt giá sàn");
        Assert.Contains(rows, n => n.Title == "Tạm khóa quyền đặt giá sàn");
        Assert.All(rows, row => Assert.Equal(NotificationType.AuctionReserveRestriction, row.NotificationType));
    }

    [PostgresFact]
    public async Task ReserveEndpoints_RejectWrongAuthAndInvalidSnapshots()
    {
        var now = DateTimeOffset.UtcNow;
        var approval = new AuctionReserveApprovalNeededRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Reserve probe", 14000000, 15000000, "VND", now.AddHours(12));
        var restriction = new AuctionReserveRestrictionRequest(Guid.NewGuid(), Guid.NewGuid(), 2, false, null, now);
        foreach (var (path, request) in new (string, object)[]
        {
            ("auction-reserve-approval-needed", approval), ("auction-reserve-restriction", restriction)
        })
        {
            _http.DefaultRequestHeaders.Authorization = null;
            Assert.Equal(HttpStatusCode.Unauthorized, (await _http.PostAsJsonAsync(Route(path), request)).StatusCode);
            _http.DefaultRequestHeaders.Authorization = AuctionServiceTestToken.Create("wrong.scope");
            Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsJsonAsync(Route(path), request)).StatusCode);
        }
        _http.DefaultRequestHeaders.Authorization = AuctionServiceTestToken.Create();
        foreach (var request in new[]
        {
            approval with { HighestBidAmount = 15000000 }, approval with { HighestBidAmount = 0 },
            approval with { SellerUserId = Guid.Empty }, approval with { ApprovalDeadlineAt = default },
            approval with { Currency = "USD" }
        })
            Assert.Equal(HttpStatusCode.BadRequest,
                (await _http.PostAsJsonAsync(Route("auction-reserve-approval-needed"), request)).StatusCode);
        foreach (var request in new[]
        {
            restriction with { ConsecutiveDeclineCount = 1 }, restriction with { IsRestricted = null },
            restriction with { IsRestricted = true }, restriction with { RestrictedUntil = now.AddDays(14) },
            restriction with { ConsecutiveDeclineCount = 3, IsRestricted = true, RestrictedUntil = now.AddSeconds(-1) }
        })
            Assert.Equal(HttpStatusCode.BadRequest,
                (await _http.PostAsJsonAsync(Route("auction-reserve-restriction"), request)).StatusCode);
    }

    private AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection).Options);
    private static string Route(string name) => "/api/v1/notifications/" + name;

    [PostgresFact]
    public async Task Watchers_ConcurrentReplayAndChangedRecipients_CreateOnePrivateItemPerUser()
    {
        var users = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var now = DateTimeOffset.UtcNow;
        var request = new AuctionWatchlistBidRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Camera", 100000, "VND", users, now, now.AddHours(1));
        var calls = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => _http.PostAsJsonAsync(Route("auction-watchlist-bids"), request)));
        Assert.All(calls, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var replay = await _http.PostAsJsonAsync(Route("auction-watchlist-bids"), request with
        {
            WatcherUserIds = users.Reverse().ToArray(), OccurredAt = now.ToOffset(TimeSpan.FromHours(7)),
            TraceId = "retry"
        });
        Assert.True((await replay.Content.ReadFromJsonAsync<AuctionActivityResponse>())!.IsReplay);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsJsonAsync(Route("auction-watchlist-bids"),
            request with { WatcherUserIds = [Guid.NewGuid()] })).StatusCode);
        await using var db = Db();
        var saved = await db.Notifications.Where(n => n.SourceEventId == request.SourceEventId).ToListAsync();
        Assert.Equal(2, saved.Count);
        Assert.All(saved, n => Assert.True(users.Contains(n.UserId) && n.IsInboxVisible && !n.IsRead));
        Assert.All(saved, n => Assert.DoesNotContain(users.First(id => id != n.UserId).ToString(), n.Payload!));
    }

    [PostgresFact]
    public async Task DepositAndReport_ArePersistedWithoutPublishingInternalAdminReason()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var status in new[] { "Authorized", "Released", "Captured" })
        {
            var deposit = new AuctionDepositRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "Camera", 100000, "VND", status, "Test reason", now);
            Assert.Equal(HttpStatusCode.OK,
                (await _http.PostAsJsonAsync(Route("auction-deposits"), deposit)).StatusCode);
        }
        var report = new AuctionReportResolvedRequest(Guid.NewGuid(), Guid.NewGuid(), "Camera",
            "ActionTaken", "Your report was processed.", "Internal investigation detail",
            [Guid.NewGuid(), Guid.NewGuid()], now);
        var result = await _http.PostAsJsonAsync(Route("auction-report-resolved"), report);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        await using var db = Db();
        var rows = await db.Notifications.Where(n => n.SourceEventId == report.SourceEventId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.DoesNotContain("Internal investigation detail", row.Payload!));
    }

    [PostgresFact]
    public async Task AuthenticationAndInvalidPaymentStates_AreRejectedBeforeWriting()
    {
        var request = new AuctionDepositRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Camera", 100000, "VND", "Pending", null, DateTimeOffset.UtcNow);
        _http.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _http.PostAsJsonAsync(Route("auction-deposits"), request)).StatusCode);
        _http.DefaultRequestHeaders.Authorization = AuctionServiceTestToken.Create("wrong");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _http.PostAsJsonAsync(Route("auction-deposits"), request)).StatusCode);
        _http.DefaultRequestHeaders.Authorization = AuctionServiceTestToken.Create();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _http.PostAsJsonAsync(Route("auction-deposits"), request)).StatusCode);
        await using var db = Db();
        Assert.Empty(await db.Notifications.Where(n => n.SourceEventId == request.SourceEventId).ToListAsync());
    }
}
