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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Notification.Api.Common;
using Notification.Api.Controllers.Public;
using Notification.Api.Middleware;
using Notification.Application;
using Notification.Application.Interfaces;
using Notification.Application.Services.Deliveries;
using Notification.Application.Services.Dispatches;
using Notification.Application.Services.AccountStatusChanges;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class AccountStatusChangeApiIntegrationTests : IAsyncLifetime
{
    private const string Route = "/api/v1/notifications/account-status-changes";
    private string? _admin,_database,_connection;
    private WebApplication? _app;
    private HttpClient _client = null!;
    private readonly Clock _clock = new();
    private readonly FakeSender _sender = new();
    private AppDbContext Db(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }
    private AccountStatusChangeService Service(AppDbContext db) => new(db,new DispatchService(db),_clock);
    private CreateAccountStatusChangeRequest Request(string change = "Banned") => new()
    {
        SourceEventId = Guid.NewGuid(),UserId = Guid.NewGuid(),StatusVersion = 1,Email = "owner@example.com",FullName = "<b>Owner</b>",
        Change = change,Reason = "<script>unsafe()</script> Decision reason.",ChangedAt = _clock.GetUtcNow()
    };
    private async Task<AccountStatusChangeResponse> Accept(CreateAccountStatusChangeRequest request)
    { await using var db = Db(); return await Service(db).CreateAsync(request); }
    private async Task<bool> Process()
    { await using var db = Db(); return await new DeliveryService(db,_sender,new(),_clock).ProcessNextAsync(); }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_account_status_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(AccountStatusChangesController).Assembly)
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiValidationProblemFactory.Create);
        builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connection));
        builder.Services.AddScoped<INotificationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        builder.Services.AddSingleton<TimeProvider>(_clock); builder.Services.AddApplicationServices();
        _app = builder.Build(); _app.UseGlobalExceptionHandler(); _app.UseMiddleware<UserContextMiddleware>(); _app.UseSwagger(); _app.MapControllers();
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        Role("service");
    }
    public async Task DisposeAsync()
    {
        _client?.Dispose(); if (_app is not null) await _app.DisposeAsync();
        if (_database is null || _admin is null) return;
        NpgsqlConnection.ClearAllPools(); await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("DROP DATABASE \"" + _database + "\" WITH (FORCE)",admin).ExecuteNonQueryAsync();
    }
    private void Role(string? role)
    {
        _client.DefaultRequestHeaders.Remove("x-user-id"); _client.DefaultRequestHeaders.Remove("x-user-roles");
        if (role is null) return;
        _client.DefaultRequestHeaders.Add("x-user-id",Guid.NewGuid().ToString()); _client.DefaultRequestHeaders.Add("x-user-roles",role);
    }
    private static async Task Check(HttpResponseMessage response,HttpStatusCode expected,string? code = null)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,$"Expected {expected}, got {response.StatusCode}: {text}");
        if (code is not null) Assert.Equal(code,JsonDocument.Parse(text).RootElement.GetProperty("errorCode").GetString());
    }

    [PostgresFact]
    public async Task Banned_HttpQueuesMandatoryEmail_WithoutTargetLoginOrInlineSend()
    {
        var request = Request(); var response = await _client.PostAsJsonAsync(Route,request); await Check(response,HttpStatusCode.OK);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var accepted = (await response.Content.ReadFromJsonAsync<AccountStatusChangeResponse>())!;
        Assert.Equal(request.UserId,accepted.UserId); Assert.Equal(1,accepted.StatusVersion); Assert.Equal("Pending",accepted.DeliveryStatus); Assert.Empty(_sender.Messages);
        await using var db = Db(); var row = await db.Notifications.SingleAsync(n => n.Id == accepted.NotificationId);
        Assert.Equal(request.UserId,row.UserId); Assert.Equal(NotificationType.SecurityAlert,row.NotificationType);
        Assert.Contains("tạm khóa",row.Title); Assert.Contains("&lt;b&gt;Owner&lt;/b&gt;",row.Body);
        Assert.Contains("&lt;script&gt;unsafe()&lt;/script&gt;",row.Body); Assert.DoesNotContain("<script>",row.Body); Assert.Contains("UTC",row.Body); Assert.Null(row.Payload);
        var receipt = await db.AccountStatusChangeDispatches.SingleAsync(); Assert.Equal(64,receipt.RequestHash.Length);
        var audit = Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditAction.AccountStatusChangeAccepted).ToListAsync());
        Assert.DoesNotContain("unsafe",audit.NewValues); Assert.Equal(request.UserId.ToString(),audit.UserId);
        Assert.False(db.Database.HasPendingModelChanges());
        var templates = await db.NotificationTemplates.Where(t => t.Code.StartsWith("ACCOUNT_")).ToListAsync(); Assert.Equal(2,templates.Count);
        Assert.All(templates,t => { Assert.True(t.IsMandatory); Assert.Equal(ChannelType.Email,t.ChannelCode); });
        var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route,swagger); Assert.Contains("statusVersion",swagger);
    }

    [PostgresFact]
    public async Task Unbanned_WorkerSendsDistinctRestorationNoticeAndReplayReportsSent()
    {
        var request = Request("Unbanned"); var accepted = await Accept(request);
        Assert.True(await Process()); Assert.False(await Process()); var message = Assert.Single(_sender.Messages);
        Assert.Equal(request.Email,message.Recipient); Assert.Contains("mở khóa",message.Subject); Assert.Contains("trạng thái hoạt động",message.HtmlBody);
        Assert.DoesNotContain("tạm khóa do vi phạm",message.HtmlBody); Assert.Contains("thời điểm",message.HtmlBody);
        var replay = await Accept(request); Assert.True(replay.IsReplay); Assert.Equal("Sent",replay.DeliveryStatus); Assert.Equal(accepted.NotificationId,replay.NotificationId);
    }

    [PostgresFact]
    public async Task Http_RejectsUntrustedRolesAndInvalidTransitionsOrRevisionFields()
    {
        var request = Request(); Role(null); await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "buyer","seller","staff" }) { Role(role); await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Forbidden); }
        Role("admin");
        foreach (var invalid in new[] { request with { SourceEventId = null },request with { SourceEventId = Guid.Empty },request with { UserId = Guid.Empty },
            request with { StatusVersion = null },request with { StatusVersion = 0 },request with { StatusVersion = -1 },request with { Email = "invalid" },
            request with { Email = new string('a',255) },request with { FullName = "bad\nname" },request with { Change = "0" },request with { Change = "LockedTemp" },
            request with { Change = "Deleted" },request with { ChangedAt = null },request with { ChangedAt = _clock.GetUtcNow().AddMinutes(1) },request with { TraceId = new string('x',101) } })
            await Check(await _client.PostAsJsonAsync(Route,invalid),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await using var db = Db(); Assert.Empty(await db.AccountStatusChangeDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task BothChanges_RequireUserSafeBoundedReason_AllowMultilineText()
    {
        foreach (var change in new[] { "Banned","Unbanned" })
        {
            var request = Request(change);
            foreach (var reason in new[] { ""," ",new string('a',1001),"invalid\0control" })
                await Check(await _client.PostAsJsonAsync(Route,request with { Reason = reason }),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
            await Check(await _client.PostAsJsonAsync(Route,request with { Reason = "Decision:\r\nReview\tcompleted." }),HttpStatusCode.OK);
        }
    }

    [PostgresFact]
    public async Task Http_RejectsSensitiveUnknownFields_AndOversizedBodyWithoutEcho()
    {
        const string secret = "do-not-store-sensitive-8374";
        foreach (var field in new[] { "password","otpCode","bankAccount","cccd","documents","data","templateCode" })
        {
            var body = JsonSerializer.SerializeToNode(Request())!.AsObject(); body[field] = secret;
            var response = await _client.PostAsync(Route,new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json"));
            await Check(response,HttpStatusCode.BadRequest,"VAL_INVALID_INPUT"); Assert.DoesNotContain(secret,await response.Content.ReadAsStringAsync());
        }
        await Check(await _client.PostAsync(Route,new StringContent("{\"reason\":\"" + new string('a',17000) + "\"}",Encoding.UTF8,"application/json")),HttpStatusCode.RequestEntityTooLarge,"REQUEST_BODY_TOO_LARGE");
        await using var db = Db(); Assert.Empty(await db.AccountStatusChangeDispatches.ToListAsync()); Assert.DoesNotContain(secret,JsonSerializer.Serialize(await db.AuditLogs.ToListAsync()));
    }

    [PostgresFact]
    public async Task Replay_NormalizesContentAndIgnoresTraceAndCurrentTemplateActivation()
    {
        var request = Request(); var accepted = await Accept(request);
        await using (var db = Db())
        {
            await db.NotificationTemplates.Where(t => t.Code == "ACCOUNT_BANNED_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,false));
            await db.AccountStatusChangeDispatches.ExecuteUpdateAsync(s => s.SetProperty(r => r.IsDeleted,true));
        }
        var replay = await Accept(request with { Email = " OWNER@EXAMPLE.COM ",FullName = " <b>Owner</b> ",Change = " banned ",Reason = " " + request.Reason + " ",
            ChangedAt = request.ChangedAt!.Value.ToOffset(TimeSpan.FromHours(7)),TraceId = "retry-trace" });
        Assert.True(replay.IsReplay); Assert.Equal(accepted.NotificationId,replay.NotificationId);
        await using var verify = Db(); Assert.Single(await verify.AuditLogs.Where(a => a.Action == AuditAction.AccountStatusChangeAccepted).ToListAsync());
    }

    [PostgresFact]
    public async Task SameEvent_ChangedTargetRevisionActionOrReasonConflicts()
    {
        var request = Request(); await Accept(request);
        foreach (var conflict in new[] { request with { UserId = Guid.NewGuid() },request with { StatusVersion = 2 },request with { Email = "other@example.com" },
            request with { Change = "Unbanned" },request with { Reason = "Different" },request with { ChangedAt = request.ChangedAt!.Value.AddSeconds(-1) } })
            await Check(await _client.PostAsJsonAsync(Route,conflict),HttpStatusCode.Conflict,"ACCOUNT_STATUS_CONFLICT");
    }

    [PostgresFact]
    public async Task ConcurrentSameEvent_AcceptsOnceAndReplaysSevenTimes()
    {
        var request = Request(); var results = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Accept(request))).WaitAsync(TimeSpan.FromSeconds(25));
        Assert.Single(results,r => !r.IsReplay); Assert.Single(results.Select(r => r.NotificationId).Distinct());
        await using var db = Db(); Assert.Single(await db.AccountStatusChangeDispatches.ToListAsync());
        Assert.Single(await db.Notifications.Where(n => n.SourceEventId == request.SourceEventId).ToListAsync());
        Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditAction.AccountStatusChangeAccepted).ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentSameEventDifferentAccounts_OnlyOneCanAccept()
    {
        var request = Request(); var results = await Task.WhenAll(new[] { request,request with { UserId = Guid.NewGuid() } }.Select(async r =>
        { try { await Accept(r); return 200; } catch (DomainException ex) { Assert.Equal("ACCOUNT_STATUS_CONFLICT",ex.ErrorCode); return ex.StatusCode; } }));
        Assert.Single(results,r => r == 200); Assert.Single(results,r => r == 409);
    }

    [PostgresFact]
    public async Task ConcurrentSameAccountRevisionDifferentEvents_OnlyOneOutcomeCanAccept()
    {
        var request = Request(); var results = await Task.WhenAll(new[] { request,request with { SourceEventId = Guid.NewGuid(),Change = "Unbanned" } }.Select(async r =>
        { try { await Accept(r); return 200; } catch (DomainException ex) { Assert.Equal("ACCOUNT_STATUS_ALREADY_NOTIFIED",ex.ErrorCode); return ex.StatusCode; } }));
        Assert.Single(results,r => r == 200); Assert.Single(results,r => r == 409);
        await using var db = Db(); Assert.Single(await db.AccountStatusChangeDispatches.ToListAsync());
        Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditAction.AccountStatusChangeAccepted).ToListAsync());
    }

    [PostgresFact]
    public async Task AliasCannotBypassAccountRevisionReservationAfterSoftDelete()
    {
        var request = Request(); await Accept(request);
        await using (var db = Db()) await db.AccountStatusChangeDispatches.ExecuteUpdateAsync(s => s.SetProperty(r => r.IsDeleted,true));
        await Check(await _client.PostAsJsonAsync(Route,request with { SourceEventId = Guid.NewGuid() }),HttpStatusCode.Conflict,"ACCOUNT_STATUS_ALREADY_NOTIFIED");
        Assert.True((await Accept(request)).IsReplay);
    }

    [PostgresFact]
    public async Task BanUnbanRebanAndDelayedHistory_AreDistinctRevisionNoticesWithEffectiveTime()
    {
        var first = Request() with { StatusVersion = 10 }; await Accept(first);
        await Accept(first with { SourceEventId = Guid.NewGuid(),StatusVersion = 11,Change = "Unbanned" });
        await Accept(first with { SourceEventId = Guid.NewGuid(),StatusVersion = 12 });
        await Accept(first with { SourceEventId = Guid.NewGuid(),StatusVersion = 8,ChangedAt = first.ChangedAt!.Value.AddDays(-1) });
        for (var i = 0;i < 4;i++) Assert.True(await Process()); Assert.False(await Process());
        Assert.Equal(4,_sender.Messages.Count); Assert.All(_sender.Messages,m => Assert.Contains("UTC",m.HtmlBody));
        Assert.Single(_sender.Messages,m => m.Subject.Contains("mở khóa"));
        await using var db = Db(); Assert.Equal(4,await db.AccountStatusChangeDispatches.CountAsync());
    }

    [PostgresFact]
    public async Task RevisionKey_IsScopedToAccount_NotGloballyUnique()
    {
        await Accept(Request()); await Accept(Request());
        await using var db = Db(); Assert.Equal(2,await db.AccountStatusChangeDispatches.CountAsync());
    }

    [PostgresFact]
    public async Task AuditFailure_RollsBackDispatchAndRevisionReservation()
    {
        var request = Request();
        await using (var db = Db(new FailSecondSave())) await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(request));
        await using var verify = Db(); Assert.Empty(await verify.AccountStatusChangeDispatches.ToListAsync());
        Assert.False(await verify.Notifications.IgnoreQueryFilters().AnyAsync(n => n.SourceEventId == request.SourceEventId));
        Assert.Empty(await verify.AuditLogs.Where(a => a.Action == AuditAction.AccountStatusChangeAccepted).ToListAsync());
        Assert.False(await Process()); Assert.False((await Accept(request)).IsReplay);
    }

    [PostgresFact]
    public async Task MandatorySecurityNotice_IgnoresLegacyDisabledPreference()
    {
        var request = Request(); await using (var db = Db())
        {
            db.NotificationPreferences.Add(new() { Id = Guid.NewGuid(),UserId = request.UserId!.Value,NotificationType = NotificationType.SecurityAlert,ChannelCode = ChannelType.Email,IsDisabled = true });
            await db.SaveChangesAsync();
        }
        Assert.Equal("Pending",(await Accept(request)).DeliveryStatus); Assert.True(await Process()); Assert.Single(_sender.Messages);
    }

    [PostgresFact]
    public async Task GlobalEmailDisable_PersistsSkippedAndDoesNotRequeueOnReplay()
    {
        var request = Request(); await using (var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var accepted = await Accept(request); Assert.Equal("Skipped",accepted.DeliveryStatus);
        await using (var db = Db())
        {
            Assert.True((await db.Notifications.IgnoreQueryFilters().SingleAsync(n => n.Id == accepted.NotificationId)).IsDeleted);
            await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,false));
        }
        Assert.Equal("Skipped",(await Accept(request)).DeliveryStatus); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task UnsafeTemplateMetadataOrInactiveTemplate_CannotAccept()
    {
        var request = Request(); await using var db = Db();
        foreach (var variant in new[] { 0,1,2,3 })
        {
            await db.NotificationTemplates.Where(t => t.Code == "ACCOUNT_BANNED_EMAIL").ExecuteUpdateAsync(s => s
                .SetProperty(t => t.ChannelCode,variant == 0 ? ChannelType.Push : ChannelType.Email)
                .SetProperty(t => t.NotificationType,variant == 1 ? NotificationType.Promotion : NotificationType.SecurityAlert)
                .SetProperty(t => t.IsMandatory,variant != 2).SetProperty(t => t.IsActive,variant != 3));
            await Check(await _client.PostAsJsonAsync(Route,request),variant == 3 ? HttpStatusCode.NotFound : HttpStatusCode.Conflict,
                variant == 3 ? "TEMPLATE_NOT_FOUND" : "ACCOUNT_STATUS_TEMPLATE_INVALID");
        }
        Assert.Empty(await db.AccountStatusChangeDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task MissingChannelOrInvalidPlaceholder_DoesNotReserveRevision()
    {
        var request = Request(); await using var db = Db();
        await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted,true));
        await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Conflict,"CHANNEL_NOT_FOUND");
        await db.NotificationChannels.IgnoreQueryFilters().Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted,false));
        await db.NotificationTemplates.Where(t => t.Code == "ACCOUNT_BANNED_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{Password}}"));
        await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.BadRequest,"TEMPLATE_DATA_MISSING");
        await db.NotificationTemplates.Where(t => t.Code == "ACCOUNT_BANNED_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{invalid"));
        await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Conflict,"TEMPLATE_INVALID");
        Assert.Empty(await db.AccountStatusChangeDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task DurableWorker_RetryPreservesSingleHistoryAndReplayStatus()
    {
        var request = Request(); var accepted = await Accept(request); _sender.Result = EmailDeliveryResult.Failure("SMTP_450","Test transient failure.",true);
        Assert.True(await Process()); Assert.False(await Process()); Assert.Equal("Pending",(await Accept(request)).DeliveryStatus);
        _clock.Advance(TimeSpan.FromSeconds(10)); _sender.Result = EmailDeliveryResult.Sent();
        Assert.True(await Process()); Assert.Equal("Sent",(await Accept(request)).DeliveryStatus);
        Assert.Equal(2,_sender.Messages.Count); Assert.Single(_sender.Messages.Select(m => m.MessageId).Distinct());
        await using var db = Db(); Assert.Equal(2,(await db.NotificationDeliveries.SingleAsync(d => d.Id == accepted.DeliveryId)).AttemptCount);
    }

    [PostgresFact]
    public async Task PermanentProviderFailure_DoesNotPretendSent()
    {
        var request = Request(); var accepted = await Accept(request); _sender.Result = EmailDeliveryResult.Failure("SMTP_NOT_CONFIGURED","Test missing config.");
        Assert.True(await Process()); Assert.False(await Process()); Assert.Equal("Failed",(await Accept(request)).DeliveryStatus);
        await using var db = Db(); Assert.Equal("SMTP_NOT_CONFIGURED",(await db.NotificationDeliveries.SingleAsync(d => d.Id == accepted.DeliveryId)).ErrorCode);
    }

    [PostgresFact]
    public async Task ExistingGenericDispatch_CannotBeAdoptedAsVerifiedBusinessData()
    {
        var request = Request(); await using (var db = Db()) await new DispatchService(db).CreateAsync(new()
        {
            SourceEventId = request.SourceEventId,UserId = request.UserId,Destination = "other@example.com",TemplateCode = "ACCOUNT_BANNED_EMAIL",
            Data = new() { ["FullName"] = "Other",["Reason"] = "Other",["ChangedAtUtc"] = "unknown" }
        });
        await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Conflict,"ACCOUNT_STATUS_CONFLICT");
        await using var verify = Db(); Assert.Empty(await verify.AccountStatusChangeDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task TemplateSharedLock_PreventsConcurrentTypeChangeDuringDispatch()
    {
        var request = Request(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); await using var acceptingDb = Db();
        var accepting = new AccountStatusChangeService(acceptingDb,new GatedDispatch(new DispatchService(acceptingDb),entered,release),_clock).CreateAsync(request);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var editor = Db(); await using var editTransaction = await editor.Database.BeginTransactionAsync();
            await editor.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => editor.NotificationTemplates.Where(t => t.Code == "ACCOUNT_BANNED_EMAIL")
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.NotificationType,NotificationType.Promotion)));
            Assert.Equal("55P03",Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        finally { release.TrySetResult(); }
        var accepted = await accepting.WaitAsync(TimeSpan.FromSeconds(5)); await using var verify = Db();
        Assert.Equal(NotificationType.SecurityAlert,(await verify.Notifications.SingleAsync(n => n.Id == accepted.NotificationId)).NotificationType);
    }

    private sealed class GatedDispatch(IDispatchService inner,TaskCompletionSource entered,TaskCompletionSource release) : IDispatchService
    {
        public async Task<DispatchResponse> CreateAsync(CreateDispatchRequest request,CancellationToken cancellationToken = default)
        { entered.TrySetResult(); await release.Task.WaitAsync(cancellationToken); return await inner.CreateAsync(request,cancellationToken); }
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
    private sealed class FakeSender : IEmailDeliverySender
    {
        public List<EmailDeliveryMessage> Messages { get; } = new();
        public EmailDeliveryResult Result { get; set; } = EmailDeliveryResult.Sent();
        public Task<EmailDeliveryResult> SendAsync(EmailDeliveryMessage message,CancellationToken ct = default)
        { Messages.Add(message); return Task.FromResult(Result); }
    }
    private sealed class FailSecondSave : SaveChangesInterceptor
    {
        private int _count;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct = default)
        {
            if (++_count == 2) throw new InvalidOperationException("Test audit failure.");
            return ValueTask.FromResult(result);
        }
    }
}
