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
using Notification.Application.Services.SellerDecisions;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class SellerDecisionApiIntegrationTests : IAsyncLifetime
{
    private const string Route = "/api/v1/notifications/seller-decisions";
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
    private SellerDecisionService Service(AppDbContext db) => new(db,new DispatchService(db),_clock);
    private CreateSellerDecisionRequest Request(string decision = "Approved") => new()
    {
        SourceEventId = Guid.NewGuid(),ApplicationId = Guid.NewGuid(),UserId = Guid.NewGuid(),Email = "owner@example.com",
        FullName = "<b>Owner</b>",ShopName = "<i>Shop</i>",Decision = decision,
        RejectionReason = decision == "Rejected" ? "<script>unsafe()</script> Missing approved details." : null,DecidedAt = _clock.GetUtcNow()
    };
    private async Task<SellerDecisionResponse> Accept(CreateSellerDecisionRequest request)
    { await using var db = Db(); return await Service(db).CreateAsync(request); }
    private async Task<bool> Process()
    { await using var db = Db(); return await new DeliveryService(db,_sender,new(),_clock).ProcessNextAsync(); }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_seller_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(SellerDecisionsController).Assembly)
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
    public async Task Approved_HttpQueuesRoleDecisionEmail_WithAtomicReceiptAuditAndSchema()
    {
        var request = Request(); var response = await _client.PostAsJsonAsync(Route,request); await Check(response,HttpStatusCode.OK);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var accepted = (await response.Content.ReadFromJsonAsync<SellerDecisionResponse>())!;
        Assert.Equal(request.ApplicationId,accepted.ApplicationId); Assert.Equal("Pending",accepted.DeliveryStatus); Assert.False(accepted.IsReplay); Assert.Empty(_sender.Messages);
        await using var db = Db(); var row = await db.Notifications.SingleAsync(n => n.Id == accepted.NotificationId);
        Assert.Equal(request.UserId,row.UserId); Assert.Equal(NotificationType.SecurityAlert,row.NotificationType);
        Assert.Contains("chấp thuận",row.Title); Assert.Contains("&lt;b&gt;Owner&lt;/b&gt;",row.Body); Assert.Contains("&lt;i&gt;Shop&lt;/i&gt;",row.Body);
        Assert.Contains("cấp thêm quyền Seller",row.Body); Assert.Contains("UTC",row.Body); Assert.Null(row.Payload);
        var receipt = await db.SellerDecisionDispatches.SingleAsync(); Assert.Equal(64,receipt.RequestHash.Length);
        var audit = Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditAction.SellerDecisionAccepted).ToListAsync());
        Assert.Equal(request.UserId.ToString(),audit.UserId); Assert.Contains(request.ApplicationId.ToString()!,audit.NewValues);
        Assert.False(db.Database.HasPendingModelChanges());
        var templates = await db.NotificationTemplates.Where(t => t.Code.StartsWith("SELLER_")).ToListAsync();
        Assert.Equal(2,templates.Count); Assert.All(templates,t => { Assert.True(t.IsMandatory); Assert.Equal(ChannelType.Email,t.ChannelCode); });
        var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route,swagger); Assert.Contains("rejectionReason",swagger);
    }

    [PostgresFact]
    public async Task Rejected_WorkerEncodesReason_ReportsProviderAcceptanceWithoutGrantingRole()
    {
        var request = Request("Rejected"); var accepted = await Accept(request);
        Assert.True(await Process()); Assert.False(await Process());
        var message = Assert.Single(_sender.Messages); Assert.Equal(request.Email,message.Recipient);
        Assert.Contains("từ chối",message.HtmlBody); Assert.Contains("&lt;script&gt;unsafe()&lt;/script&gt;",message.HtmlBody);
        Assert.DoesNotContain("<script>",message.HtmlBody); Assert.DoesNotContain("cấp thêm quyền Seller",message.HtmlBody);
        var replay = await Accept(request); Assert.True(replay.IsReplay); Assert.Equal("Sent",replay.DeliveryStatus); Assert.Equal(accepted.NotificationId,replay.NotificationId);
        await using var db = Db(); var audit = await db.AuditLogs.Where(a => a.Action == AuditAction.SellerDecisionAccepted).SingleAsync();
        Assert.DoesNotContain("unsafe",audit.NewValues); Assert.DoesNotContain(request.RejectionReason!,JsonSerializer.Serialize(await db.SellerDecisionDispatches.ToListAsync()));
    }

    [PostgresFact]
    public async Task Http_RejectsUntrustedRolesAndInvalidFields()
    {
        var request = Request(); Role(null); await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "buyer","seller","staff" }) { Role(role); await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Forbidden); }
        Role("admin");
        foreach (var invalid in new[] { request with { SourceEventId = null },request with { SourceEventId = Guid.Empty },request with { ApplicationId = Guid.Empty },
            request with { UserId = null },request with { UserId = Guid.Empty },request with { Email = "invalid" },request with { ShopName = " " },
            request with { ShopName = new string('a',201) },request with { FullName = "bad\nname" },request with { Decision = "0" },request with { Decision = "Pending" },
            request with { DecidedAt = null },request with { DecidedAt = _clock.GetUtcNow().AddMinutes(1) },request with { TraceId = new string('x',101) },
            request with { RejectionReason = "not applicable" } })
            await Check(await _client.PostAsJsonAsync(Route,invalid),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await using var db = Db(); Assert.Empty(await db.SellerDecisionDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task Rejection_RequiresBoundedReason_AndAllowsUserFacingMultilineText()
    {
        var request = Request("Rejected");
        foreach (var reason in new string?[] { null," ",new string('a',1001),"invalid\0control" })
            await Check(await _client.PostAsJsonAsync(Route,request with { RejectionReason = reason }),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await Check(await _client.PostAsJsonAsync(Route,request with { RejectionReason = "Please correct:\r\nDetails\tneeded." }),HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task Http_RejectsPrivateDocumentAndSecretMembers_AndOversizedBody()
    {
        const string secret = "do-not-persist-sensitive-6243";
        foreach (var field in new[] { "bankAccount","cccd","documents","password","otpCode","data","templateCode" })
        {
            var body = JsonSerializer.SerializeToNode(Request())!.AsObject(); body[field] = secret;
            var response = await _client.PostAsync(Route,new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json"));
            await Check(response,HttpStatusCode.BadRequest,"VAL_INVALID_INPUT"); Assert.DoesNotContain(secret,await response.Content.ReadAsStringAsync());
        }
        await Check(await _client.PostAsync(Route,new StringContent("{\"shopName\":\"" + new string('a',17000) + "\"}",Encoding.UTF8,"application/json")),HttpStatusCode.RequestEntityTooLarge,"REQUEST_BODY_TOO_LARGE");
        await using var db = Db(); Assert.Empty(await db.SellerDecisionDispatches.ToListAsync());
        Assert.DoesNotContain(secret,JsonSerializer.Serialize(await db.AuditLogs.ToListAsync()));
    }

    [PostgresFact]
    public async Task Replay_NormalizesBodyAndIgnoresTrace_WithoutDependingOnCurrentTemplate()
    {
        var request = Request("Rejected"); var accepted = await Accept(request);
        await using (var db = Db())
        {
            await db.NotificationTemplates.Where(t => t.Code == "SELLER_REJECTED_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,false));
            await db.SellerDecisionDispatches.ExecuteUpdateAsync(s => s.SetProperty(r => r.IsDeleted,true));
        }
        var replay = await Accept(request with { Email = " OWNER@EXAMPLE.COM ",FullName = " <b>Owner</b> ",ShopName = " <i>Shop</i> ",Decision = " rejected ",
            RejectionReason = " " + request.RejectionReason + " ",DecidedAt = request.DecidedAt!.Value.ToOffset(TimeSpan.FromHours(7)),TraceId = "retry-trace" });
        Assert.True(replay.IsReplay); Assert.Equal(accepted.NotificationId,replay.NotificationId);
        await using var verify = Db(); Assert.Single(await verify.AuditLogs.Where(a => a.Action == AuditAction.SellerDecisionAccepted).ToListAsync());
    }

    [PostgresFact]
    public async Task SameEvent_WithChangedTargetApplicationDecisionOrReason_Conflicts()
    {
        var request = Request("Rejected"); await Accept(request);
        foreach (var conflict in new[] { request with { UserId = Guid.NewGuid() },request with { ApplicationId = Guid.NewGuid() },request with { Email = "other@example.com" },
            request with { ShopName = "Different" },request with { Decision = "Approved",RejectionReason = null },request with { RejectionReason = "Different" },
            request with { DecidedAt = request.DecidedAt!.Value.AddSeconds(-1) } })
            await Check(await _client.PostAsJsonAsync(Route,conflict),HttpStatusCode.Conflict,"SELLER_DECISION_CONFLICT");
    }

    [PostgresFact]
    public async Task ConcurrentSameEvent_AcceptsOnceAndReplaysSevenTimes()
    {
        var request = Request(); var results = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Accept(request))).WaitAsync(TimeSpan.FromSeconds(25));
        Assert.Single(results,r => !r.IsReplay); Assert.Single(results.Select(r => r.NotificationId).Distinct());
        await using var db = Db(); Assert.Single(await db.SellerDecisionDispatches.ToListAsync());
        Assert.Single(await db.Notifications.Where(n => n.SourceEventId == request.SourceEventId).ToListAsync());
        Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditAction.SellerDecisionAccepted).ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentSameEventDifferentApplications_OneWinsAndOtherConflicts()
    {
        var request = Request();
        var results = await Task.WhenAll(new[] { request,request with { ApplicationId = Guid.NewGuid(),UserId = Guid.NewGuid(),Email = "other@example.com" } }.Select(async r =>
        { try { await Accept(r); return 200; } catch (DomainException ex) { Assert.Equal("SELLER_DECISION_CONFLICT",ex.ErrorCode); return ex.StatusCode; } }));
        Assert.Single(results,r => r == 200); Assert.Single(results,r => r == 409);
        await using var db = Db(); Assert.Single(await db.SellerDecisionDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentSameApplicationDifferentEvents_OnlyOneFinalOutcomeIsAccepted()
    {
        var request = Request();
        var results = await Task.WhenAll(new[] { request,request with { SourceEventId = Guid.NewGuid(),Decision = "Rejected",RejectionReason = "Valid rejection." } }.Select(async r =>
        { try { await Accept(r); return 200; } catch (DomainException ex) { Assert.Equal("SELLER_APPLICATION_ALREADY_NOTIFIED",ex.ErrorCode); return ex.StatusCode; } }));
        Assert.Single(results,r => r == 200); Assert.Single(results,r => r == 409);
        await using var db = Db(); var receipt = await db.SellerDecisionDispatches.SingleAsync();
        Assert.Single(await db.NotificationDeliveries.Where(d => d.NotificationId == receipt.NotificationId).ToListAsync());
        Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditAction.SellerDecisionAccepted).ToListAsync());
    }

    [PostgresFact]
    public async Task DuplicateSourceAlias_CannotBypassFinalApplicationReservationAfterSoftDelete()
    {
        var request = Request(); await Accept(request);
        await using (var db = Db()) await db.SellerDecisionDispatches.ExecuteUpdateAsync(s => s.SetProperty(r => r.IsDeleted,true));
        await Check(await _client.PostAsJsonAsync(Route,request with { SourceEventId = Guid.NewGuid() }),HttpStatusCode.Conflict,"SELLER_APPLICATION_ALREADY_NOTIFIED");
        Assert.True((await Accept(request)).IsReplay);
    }

    [PostgresFact]
    public async Task ResubmissionWithNewApplicationId_CanNotifySameUserOfNewDecision()
    {
        var old = Request("Rejected"); await Accept(old);
        var current = Request() with { UserId = old.UserId }; await Accept(current);
        Assert.True(await Process()); Assert.True(await Process()); Assert.Equal(2,_sender.Messages.Count);
        await using var db = Db(); Assert.Equal(2,await db.SellerDecisionDispatches.CountAsync());
    }

    [PostgresFact]
    public async Task AuditFailure_RollsBackDispatchReceiptAndApplicationReservation()
    {
        var request = Request();
        await using (var db = Db(new FailSecondSave())) await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(request));
        await using var verify = Db(); Assert.Empty(await verify.SellerDecisionDispatches.ToListAsync());
        Assert.False(await verify.Notifications.IgnoreQueryFilters().AnyAsync(n => n.SourceEventId == request.SourceEventId));
        Assert.Empty(await verify.AuditLogs.Where(a => a.Action == AuditAction.SellerDecisionAccepted).ToListAsync());
        Assert.False(await Process()); Assert.False((await Accept(request)).IsReplay);
    }

    [PostgresFact]
    public async Task MandatoryAccountDecision_IgnoresLegacyDisabledPreference()
    {
        var request = Request();
        await using (var db = Db())
        {
            db.NotificationPreferences.Add(new() { Id = Guid.NewGuid(),UserId = request.UserId!.Value,NotificationType = NotificationType.SecurityAlert,ChannelCode = ChannelType.Email,IsDisabled = true });
            await db.SaveChangesAsync();
        }
        Assert.Equal("Pending",(await Accept(request)).DeliveryStatus); Assert.True(await Process()); Assert.Single(_sender.Messages);
    }

    [PostgresFact]
    public async Task GlobalEmailDisable_PersistsSkipped_AndReplayDoesNotRequeueAfterEnable()
    {
        var request = Request();
        await using (var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var accepted = await Accept(request); Assert.Equal("Skipped",accepted.DeliveryStatus);
        await using (var db = Db())
        {
            Assert.True((await db.Notifications.IgnoreQueryFilters().SingleAsync(n => n.Id == accepted.NotificationId)).IsDeleted);
            await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,false));
        }
        Assert.Equal("Skipped",(await Accept(request)).DeliveryStatus); Assert.False(await Process()); Assert.Empty(_sender.Messages);
    }

    [PostgresFact]
    public async Task UnsafeBusinessTemplateConfiguration_IsRejectedBeforeDispatch()
    {
        var request = Request(); await using var db = Db();
        foreach (var variant in new[] { 0,1,2,3 })
        {
            await db.NotificationTemplates.Where(t => t.Code == "SELLER_APPROVED_EMAIL").ExecuteUpdateAsync(s => s
                .SetProperty(t => t.ChannelCode,variant == 0 ? ChannelType.Push : ChannelType.Email)
                .SetProperty(t => t.NotificationType,variant == 1 ? NotificationType.Promotion : NotificationType.SecurityAlert)
                .SetProperty(t => t.IsMandatory,variant != 2).SetProperty(t => t.IsActive,variant != 3));
            await Check(await _client.PostAsJsonAsync(Route,request),variant == 3 ? HttpStatusCode.NotFound : HttpStatusCode.Conflict,
                variant == 3 ? "TEMPLATE_NOT_FOUND" : "SELLER_DECISION_TEMPLATE_INVALID");
        }
        Assert.Empty(await db.SellerDecisionDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task MissingChannelAndInvalidPlaceholder_DoNotReserveApplication()
    {
        var request = Request(); await using var db = Db();
        await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted,true));
        await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Conflict,"CHANNEL_NOT_FOUND");
        await db.NotificationChannels.IgnoreQueryFilters().Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted,false));
        await db.NotificationTemplates.Where(t => t.Code == "SELLER_APPROVED_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{BankAccount}}"));
        await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.BadRequest,"TEMPLATE_DATA_MISSING");
        await db.NotificationTemplates.Where(t => t.Code == "SELLER_APPROVED_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{invalid"));
        await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Conflict,"TEMPLATE_INVALID");
        Assert.Empty(await db.SellerDecisionDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task DurableWorker_TransientRetryUpdatesReplayWithoutDuplicateHistory()
    {
        var request = Request(); var accepted = await Accept(request);
        _sender.Result = EmailDeliveryResult.Failure("SMTP_450","Test temporary failure.",true);
        Assert.True(await Process()); Assert.Equal("Pending",(await Accept(request)).DeliveryStatus); Assert.False(await Process());
        _clock.Advance(TimeSpan.FromSeconds(10)); _sender.Result = EmailDeliveryResult.Sent();
        Assert.True(await Process()); Assert.Equal("Sent",(await Accept(request)).DeliveryStatus);
        Assert.Equal(2,_sender.Messages.Count); Assert.Single(_sender.Messages.Select(m => m.MessageId).Distinct());
        await using var db = Db(); Assert.Equal(2,(await db.NotificationDeliveries.SingleAsync(d => d.Id == accepted.DeliveryId)).AttemptCount);
        Assert.Single(await db.SellerDecisionDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task DurableWorker_PermanentFailureNeverReportsSent()
    {
        var request = Request("Rejected"); var accepted = await Accept(request);
        _sender.Result = EmailDeliveryResult.Failure("SMTP_NOT_CONFIGURED","Test missing configuration.");
        Assert.True(await Process()); Assert.Equal("Failed",(await Accept(request)).DeliveryStatus); Assert.False(await Process());
        await using var db = Db(); Assert.Equal("SMTP_NOT_CONFIGURED",(await db.NotificationDeliveries.SingleAsync(d => d.Id == accepted.DeliveryId)).ErrorCode);
    }

    [PostgresFact]
    public async Task ExistingGenericDispatch_IsNotAdoptedAsKnownBusinessOutcome()
    {
        var request = Request();
        await using (var db = Db()) await new DispatchService(db).CreateAsync(new()
        {
            SourceEventId = request.SourceEventId,UserId = request.UserId,Destination = "wrong@example.com",TemplateCode = "SELLER_APPROVED_EMAIL",
            Data = new() { ["FullName"] = "Other",["ShopName"] = "Other",["DecidedAtUtc"] = "unknown" }
        });
        await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Conflict,"SELLER_DECISION_CONFLICT");
        await using var verify = Db(); Assert.Empty(await verify.SellerDecisionDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task TemplateLock_PreventsConcurrentTypeChangeDuringDispatch()
    {
        var request = Request(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var acceptingDb = Db();
        var accepting = new SellerDecisionService(acceptingDb,new GatedDispatch(new DispatchService(acceptingDb),entered,release),_clock).CreateAsync(request);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var editor = Db(); await using var editTransaction = await editor.Database.BeginTransactionAsync();
            await editor.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => editor.NotificationTemplates.Where(t => t.Code == "SELLER_APPROVED_EMAIL")
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.NotificationType,NotificationType.Promotion)));
            Assert.Equal("55P03",Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        finally { release.TrySetResult(); }
        var accepted = await accepting.WaitAsync(TimeSpan.FromSeconds(5));
        await using var verify = Db(); Assert.Equal(NotificationType.SecurityAlert,(await verify.Notifications.SingleAsync(n => n.Id == accepted.NotificationId)).NotificationType);
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
