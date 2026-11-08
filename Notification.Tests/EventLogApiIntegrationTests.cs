namespace Notification.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Notification.Api.Common;
using Notification.Api.Controllers.Public;
using Notification.Api.Middleware;
using Notification.Application;
using Notification.Application.Interfaces;
using Notification.Application.Services.EventLogs;
using Notification.Domain.Entities;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class EventLogApiIntegrationTests : IAsyncLifetime
{
    private const string Ingest = "/api/v1/notifications/event-logs";
    private const string Browse = "/api/v1/admin/notifications/event-logs";
    private string? _admin, _database, _connection;
    private WebApplication? _app;
    private HttpClient _client = null!;
    private AppDbContext Db(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_eventlog_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"", admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(EventLogsController).Assembly)
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiValidationProblemFactory.Create);
        builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connection));
        builder.Services.AddScoped<INotificationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddApplicationServices();
        _app = builder.Build();
        _app.UseGlobalExceptionHandler(); _app.UseMiddleware<UserContextMiddleware>(); _app.UseSwagger(); _app.MapControllers();
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
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("DROP DATABASE \"" + _database + "\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }
    private void Role(string? role)
    {
        _client.DefaultRequestHeaders.Remove("x-user-id"); _client.DefaultRequestHeaders.Remove("x-user-roles");
        if (role is null) return;
        _client.DefaultRequestHeaders.Add("x-user-id", Guid.NewGuid().ToString());
        _client.DefaultRequestHeaders.Add("x-user-roles", role);
    }
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static CreateEventLogRequest Request() => new()
    {
        EventId = Guid.NewGuid(), SourceService = "commerce", EventType = "OrderPaid",
        OccurredAt = DateTimeOffset.Parse("2026-11-08T00:00:00Z"), ActorId = Guid.NewGuid(), ActorRole = "buyer",
        TargetId = Guid.NewGuid(), TraceId = "source-trace", Headers = new() { ["x-correlation-id"] = "order-123" },
        Payload = Json("""{"z":2,"a":{"nested":[true,null,"private-payload"]}}""")
    };
    private static async Task Code(HttpResponseMessage response, HttpStatusCode expected, string? code = null)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {text}");
        if (code is not null) Assert.Equal(code, Json(text).GetProperty("errorCode").GetString());
    }
    private async Task<EventLogAcceptanceResponse> Post(CreateEventLogRequest request)
    {
        var response = await _client.PostAsJsonAsync(Ingest, request);
        await Code(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<EventLogAcceptanceResponse>())!;
    }

    [PostgresFact]
    public async Task Roles_ProtectIngestionAndSensitiveReads()
    {
        var request = Request();
        Role(null);
        await Code(await _client.PostAsJsonAsync(Ingest, request), HttpStatusCode.Unauthorized);
        await Code(await _client.GetAsync(Browse), HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "buyer", "seller", "staff" })
        {
            Role(role); await Code(await _client.PostAsJsonAsync(Ingest, request), HttpStatusCode.Forbidden);
        }
        Role("service"); var row = await Post(request);
        await Code(await _client.GetAsync(Browse), HttpStatusCode.Forbidden);
        await Code(await _client.GetAsync(Browse + "/" + row.Id), HttpStatusCode.Forbidden);
        foreach (var role in new[] { "buyer", "seller" })
        {
            Role(role); await Code(await _client.GetAsync(Browse + "/" + row.Id), HttpStatusCode.Forbidden);
        }
        Role("staff"); await Code(await _client.GetAsync(Browse), HttpStatusCode.OK);
        await Code(await _client.GetAsync(Browse + "/" + row.Id), HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task Detail_PreservesStructuredPayloadTraceAndOnlyDeclaredHeaders()
    {
        _client.DefaultRequestHeaders.Add("Authorization", "Bearer never-store-this");
        _client.DefaultRequestHeaders.Add("Cookie", "session=never-store-this");
        var request = Request(); var row = await Post(request);
        using var document = JsonDocument.Parse(await _client.GetStringAsync(Browse + "/" + row.Id));
        var json = document.RootElement;
        Assert.Equal("private-payload", json.GetProperty("payload").GetProperty("a").GetProperty("nested")[2].GetString());
        Assert.Equal("source-trace", json.GetProperty("summary").GetProperty("traceId").GetString());
        Assert.Equal("order-123", json.GetProperty("headers").GetProperty("x-correlation-id").GetString());
        Assert.DoesNotContain("never-store-this", json.GetRawText()); Assert.DoesNotContain("requestHash", json.GetRawText());
        var list = await _client.GetStringAsync(Browse);
        Assert.DoesNotContain("private-payload", list); Assert.DoesNotContain("headers", list); Assert.DoesNotContain("payload", list);
        await using var db = Db();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(64, (await db.EventLogs.SingleAsync(e => e.Id == row.Id)).RequestHash!.Length);
    }

    [PostgresFact]
    public async Task Replay_NormalizesObjectOrderingSourceHeadersAndUtcPrecision()
    {
        var request = Request(); var first = await Post(request);
        Role("service"); // Different ingestion principal and HTTP trace must not change event identity.
        var replay = await _client.PostAsJsonAsync(Ingest, request with
        {
            SourceService = " COMMERCE ", OccurredAt = request.OccurredAt!.Value.ToOffset(TimeSpan.FromHours(7)).AddTicks(1),
            Headers = new() { ["X-Correlation-Id"] = "order-123" },
            Payload = Json("""{ "a": { "nested": [ true, null, "private-payload" ] }, "z": 2 }""")
        });
        await Code(replay, HttpStatusCode.OK);
        Assert.Equal(first with { IsReplay = true }, await replay.Content.ReadFromJsonAsync<EventLogAcceptanceResponse>());
        await using var db = Db(); Assert.Single(await db.EventLogs.ToListAsync());
    }

    [PostgresFact]
    public async Task Conflicts_IncludeEveryEnvelopeField_AndDifferentSourcesMayReuseEventId()
    {
        var request = Request(); await Post(request);
        foreach (var changed in new[]
        {
            request with { EventType = "OrderCancelled" }, request with { Payload = Json("""{"z":2.0}""") },
            request with { TraceId = "other" }, request with { ActorId = Guid.NewGuid() },
            request with { ActorRole = "seller" }, request with { TargetId = Guid.NewGuid() },
            request with { OccurredAt = request.OccurredAt!.Value.AddSeconds(1) },
            request with { Headers = new() { ["x-correlation-id"] = "other" } }
        })
            await Code(await _client.PostAsJsonAsync(Ingest, changed), HttpStatusCode.Conflict, "EVENT_LOG_CONFLICT");
        await Post(request with { SourceService = "auction" });
        await using var db = Db(); Assert.Equal(2, await db.EventLogs.CountAsync());
    }

    [PostgresFact]
    public async Task Filters_UtcInclusiveAndStablePagination_EmptyResultsAreValid()
    {
        var request = Request();
        var first = await Post(request);
        var second = await Post(request with { EventId = Guid.NewGuid(), OccurredAt = request.OccurredAt!.Value.AddHours(1) });
        await Post(Request() with { SourceService = "auction", EventType = "BidOutbid" });
        var query = $"?sourceService=COMMERCE&eventType=OrderPaid&actorId={request.ActorId}&targetId={request.TargetId}&traceId=source-trace&pageSize=1";
        var page = Json(await _client.GetStringAsync(Browse + query));
        Assert.Equal(second.Id, page.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(2, page.GetProperty("pagination").GetProperty("totalCount").GetInt32());
        var next = Json(await _client.GetStringAsync(Browse + query + "&page=2"));
        Assert.Equal(first.Id, next.GetProperty("items")[0].GetProperty("id").GetGuid());
        var exact = Json(await _client.GetStringAsync(Browse + "?from=2026-11-08T07:00:00%2B07:00&to=2026-11-08T07:00:00%2B07:00&eventId=" + request.EventId));
        Assert.Single(exact.GetProperty("items").EnumerateArray());
        var empty = Json(await _client.GetStringAsync(Browse + "?eventId=" + Guid.NewGuid()));
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
    }

    [PostgresFact]
    public async Task Validation_RejectsIdsMissingFieldsAndInvalidQueries()
    {
        var request = Request();
        foreach (var invalid in new[]
        {
            request with { EventId = null }, request with { EventId = Guid.Empty }, request with { OccurredAt = null },
            request with { ActorId = Guid.Empty }, request with { TargetId = Guid.Empty }, request with { EventType = "" },
            request with { SourceService = "invalid service" }, request with { TraceId = "bad\ntrace" },
            request with { ActorRole = new string('x',51) },
            request with { Payload = Json("null") }, request with { Payload = Json("[]") },
            request with { Headers = new() { ["Authorization"] = "secret" } },
            request with { Headers = new() { ["x-request-id"] = new string('x',257) } }
        })
            await Code(await _client.PostAsJsonAsync(Ingest, invalid), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT");
        await Code(await _client.PostAsync(Ingest, new StringContent(
            "{\"eventId\":\"" + request.EventId + "\",\"sourceService\":\"commerce\",\"eventType\":\"OrderPaid\",\"occurredAt\":\"2026-11-08T00:00:00Z\"}",
            Encoding.UTF8,"application/json")), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT");
        foreach (var query in new[] { "?page=0", "?pageSize=101", "?page=2147483647&pageSize=100", "?actorId=" + Guid.Empty,
            "?targetId=" + Guid.Empty, "?eventId=invalid", "?sourceService=invalid%20service",
            "?from=2026-12-01T00:00:00Z&to=2026-01-01T00:00:00Z" })
            await Code(await _client.GetAsync(Browse + query), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT");
        await Code(await _client.GetAsync(Browse + "/" + Guid.NewGuid()), HttpStatusCode.NotFound, "EVENT_LOG_NOT_FOUND");
        await Code(await _client.GetAsync(Browse + "/" + Guid.Empty), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT");
        await using var db = Db(); Assert.Empty(await db.EventLogs.ToListAsync());
    }

    [PostgresFact]
    public async Task JsonValidation_RejectsDuplicatesUnicodeNulDepthAndUnsupportedNumbers()
    {
        var request = Request();
        foreach (var payload in new[]
        {
            """{"x":1,"x":2}""", """{"nest":{"a":1,"a":2}}""", """{"x":"\u0000"}""",
            """{"x":"\uD800"}""", """{"x":1e400}""", """{"x":1e-301}""",
            "{\"x\":" + new string('[',33) + "0" + new string(']',33) + "}",
            "{\"x\":\"" + new string('a',65536) + "\"}"
        })
        {
            // Raw HTTP is necessary: HttpClient JSON serialization rejects malformed UTF-16 first.
            var envelope = JsonSerializer.Serialize(request with { Payload = Json("{}") }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            envelope = envelope.Replace("\"payload\":{}", "\"payload\":" + payload);
            await Code(await _client.PostAsync(Ingest, new StringContent(envelope,Encoding.UTF8,"application/json")), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT");
        }
        var prefix = "{\"eventId\":\"" + request.EventId + "\",\"sourceService\":\"commerce\",\"eventType\":\"OrderPaid\",\"occurredAt\":\"2026-11-08T00:00:00Z\",\"payload\":{},\"headers\":";
        foreach (var headers in new[] { """{"x-request-id":"a","x-request-id":"b"}""", """{"x-request-id":"a","X-Request-Id":"b"}""", """{"x-request-id":null}""" })
            await Code(await _client.PostAsync(Ingest, new StringContent(prefix + headers + "}", Encoding.UTF8,"application/json")), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT");
        await Code(await _client.PostAsync(Ingest, new StringContent("{invalid", Encoding.UTF8,"application/json")), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT");
    }

    [PostgresFact]
    public async Task BodyLimit_RejectsOversizedRequest()
    {
        var body = JsonSerializer.Serialize(Request() with { Payload = Json("{\"large\":\"" + new string('a',140000) + "\"}") });
        await Code(await _client.PostAsync(Ingest, new StringContent(body,Encoding.UTF8,"application/json")), HttpStatusCode.RequestEntityTooLarge, "REQUEST_BODY_TOO_LARGE");
        await using var db = Db(); Assert.Empty(await db.EventLogs.ToListAsync());
    }

    [PostgresFact]
    public async Task Concurrency_SameKeySameEnvelopeInsertsOnce()
    {
        var request = Request(); var gate = new SaveGate(8);
        var results = await Task.WhenAll(Enumerable.Range(0,8).Select(async _ =>
        {
            await using var db = Db(gate); return await new EventLogService(db, TimeProvider.System).CreateAsync(request);
        })).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results, r => !r.IsReplay); Assert.Single(results.Select(r => r.Id).Distinct());
        await using var verify = Db(); Assert.Single(await verify.EventLogs.ToListAsync());
    }

    [PostgresFact]
    public async Task Concurrency_SameKeyDifferentEnvelopesConflictsWithoutExtraRows()
    {
        var request = Request(); var gate = new SaveGate(2);
        var results = await Task.WhenAll(Enumerable.Range(0,2).Select(async i =>
        {
            await using var db = Db(gate);
            try { await new EventLogService(db, TimeProvider.System).CreateAsync(request with { EventType = "Type" + i }); return 201; }
            catch (DomainException ex) { Assert.Equal("EVENT_LOG_CONFLICT", ex.ErrorCode); return ex.StatusCode; }
        })).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results, r => r == 201); Assert.Single(results, r => r == 409);
        await using var verify = Db(); Assert.Single(await verify.EventLogs.ToListAsync());
    }

    [PostgresFact]
    public async Task SoftDeleted_ReplaysButDoesNotAppearInListOrDetail()
    {
        var request = Request(); var created = await Post(request);
        await using (var db = Db()) await db.EventLogs.Where(e => e.Id == created.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsDeleted, true));
        await Code(await _client.PostAsJsonAsync(Ingest, request), HttpStatusCode.OK);
        await Code(await _client.GetAsync(Browse + "/" + created.Id), HttpStatusCode.NotFound, "EVENT_LOG_NOT_FOUND");
        Assert.Empty(Json(await _client.GetStringAsync(Browse)).GetProperty("items").EnumerateArray());
        await using var verify = Db(); Assert.Single(await verify.EventLogs.IgnoreQueryFilters().ToListAsync());
    }

    [PostgresFact]
    public async Task LegacyRows_NullableMetadataStillReadable_ExistingKeyConflicts()
    {
        var request = Request();
        await using var db = Db();
        var legacy = new EventLog { Id = Guid.NewGuid(), EventId = request.EventId, EventType = request.EventType, SourceService = request.SourceService };
        db.EventLogs.Add(legacy); await db.SaveChangesAsync();
        var detail = Json(await _client.GetStringAsync(Browse + "/" + legacy.Id));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("payload").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("headers").ValueKind);
        await Code(await _client.PostAsJsonAsync(Ingest, request), HttpStatusCode.Conflict, "EVENT_LOG_CONFLICT");
        db.EventLogs.Add(new EventLog { Id = Guid.NewGuid(), EventType = "Legacy", SourceService = "commerce" });
        db.EventLogs.Add(new EventLog { Id = Guid.NewGuid(), EventType = "Legacy", SourceService = "commerce" });
        await db.SaveChangesAsync(); // Filtered index permits multiple legacy NULL event IDs.
    }

    [PostgresFact]
    public async Task SaveFailure_DoesNotCommitPartialEvent()
    {
        await using (var db = Db(new FailSave()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new EventLogService(db,TimeProvider.System).CreateAsync(Request()));
        await using var verify = Db(); Assert.Empty(await verify.EventLogs.ToListAsync());
    }

    [PostgresFact]
    public async Task Swagger_DescribesThreeOperationsAndQueryParameters()
    {
        var paths = Json(await _client.GetStringAsync("/swagger/v1/swagger.json")).GetProperty("paths");
        Assert.True(paths.GetProperty(Ingest).TryGetProperty("post",out _));
        Assert.True(paths.GetProperty(Browse).TryGetProperty("get",out var list));
        Assert.True(paths.GetProperty(Browse + "/{id}").TryGetProperty("get",out _));
        foreach (var name in new[] { "Page", "PageSize", "SourceService", "EventType", "ActorId", "From", "To", "TraceId" })
            Assert.Contains(list.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == name);
    }

    private sealed class SaveGate(int expected) : SaveChangesInterceptor
    {
        private int _count;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _count) == expected) _release.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken); return result;
        }
    }
    private sealed class FailSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated save failure.");
    }
}
