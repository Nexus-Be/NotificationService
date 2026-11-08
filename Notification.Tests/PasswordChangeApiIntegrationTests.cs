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
using Notification.Application.Services.PasswordChanges;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class PasswordChangeApiIntegrationTests : IAsyncLifetime
{
    private const string Route = "/api/v1/notifications/password-changes";
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
    private PasswordChangeService Service(AppDbContext db) => new(db,new DispatchService(db),_clock);
    private CreatePasswordChangeRequest Request(string kind = "Changed") => new()
    {
        SourceEventId = Guid.NewGuid(),UserId = Guid.NewGuid(),Email = "account@example.com",FullName = "<b>Customer</b>",
        Kind = kind,ChangedAt = _clock.GetUtcNow()
    };
    private async Task<PasswordChangeResponse> Accept(CreatePasswordChangeRequest request)
    { await using var db = Db(); return await Service(db).CreateAsync(request); }
    private async Task<bool> Process()
    { await using var db = Db(); return await new DeliveryService(db,_sender,new(),_clock).ProcessNextAsync(); }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_password_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(PasswordChangesController).Assembly)
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
    public async Task Changed_HttpPersistsMandatoryEmailAndAudit_WithoutSendingInline()
    {
        var request = Request(); var response = await _client.PostAsJsonAsync(Route,request); await Check(response,HttpStatusCode.OK);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var accepted = (await response.Content.ReadFromJsonAsync<PasswordChangeResponse>())!;
        Assert.Equal(request.SourceEventId,accepted.SourceEventId); Assert.Equal("Pending",accepted.DeliveryStatus); Assert.False(accepted.IsReplay);
        Assert.Empty(_sender.Messages);
        await using var db = Db();
        var row = await db.Notifications.SingleAsync(n => n.Id == accepted.NotificationId);
        Assert.Equal(request.UserId,row.UserId); Assert.Equal(NotificationType.SecurityAlert,row.NotificationType);
        Assert.Contains("thay đổi",row.Title); Assert.Contains("&lt;b&gt;Customer&lt;/b&gt;",row.Body); Assert.Contains("UTC",row.Body);
        Assert.Contains("không thực hiện",row.Body); Assert.Null(row.Payload);
        var receipt = await db.PasswordChangeDispatches.SingleAsync(); Assert.Equal(64,receipt.RequestHash.Length);
        var audit = Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditAction.PasswordChangeAccepted).ToListAsync());
        Assert.Equal(request.UserId.ToString(),audit.UserId); Assert.Contains(accepted.NotificationId.ToString(),audit.NewValues);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.All(await db.NotificationTemplates.Where(t => t.Code.StartsWith("PASSWORD_")).ToListAsync(),t => Assert.True(t.IsMandatory));
        var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route,swagger); Assert.Contains("changedAt",swagger);
    }

    [PostgresFact]
    public async Task Reset_WorkerSendsDistinctConfirmation_AndReplayReportsCurrentStatus()
    {
        var request = Request("Reset"); var accepted = await Accept(request);
        Assert.True(await Process()); Assert.False(await Process());
        var message = Assert.Single(_sender.Messages); Assert.Equal("account@example.com",message.Recipient);
        Assert.Contains("đặt lại",message.Subject); Assert.Contains("đặt lại",message.HtmlBody); Assert.DoesNotContain("thay đổi",message.Subject);
        var replay = await Accept(request); Assert.True(replay.IsReplay); Assert.Equal("Sent",replay.DeliveryStatus); Assert.Equal("Completed",replay.NotificationStatus);
        Assert.Equal(accepted.NotificationId,replay.NotificationId); Assert.Single(_sender.Messages);
    }

    [PostgresFact]
    public async Task Http_RejectsUntrustedRolesAndInvalidFields()
    {
        var request = Request(); Role(null); await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "buyer","seller","staff" })
        { Role(role); await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Forbidden); }
        Role("admin");
        foreach (var invalid in new[] { request with { SourceEventId = null },request with { SourceEventId = Guid.Empty },request with { UserId = Guid.Empty },
            request with { Email = "invalid" },request with { Email = new string('a',255) + "@example.com" },request with { FullName = "bad\nname" },
            request with { Kind = "0" },request with { Kind = "requested" },request with { ChangedAt = null },
            request with { ChangedAt = _clock.GetUtcNow().AddMinutes(1) },request with { TraceId = new string('a',101) } })
            await Check(await _client.PostAsJsonAsync(Route,invalid),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await using var db = Db(); Assert.Empty(await db.PasswordChangeDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task Http_RejectsSecretAndUnknownMembers_WithoutPersistingOrEchoingValues()
    {
        const string secret = "never-persist-this-secret-9248";
        foreach (var field in new[] { "password","newPassword","passwordHash","otpCode","data","unexpected" })
        {
            var body = JsonSerializer.SerializeToNode(Request())!.AsObject(); body[field] = secret;
            var response = await _client.PostAsync(Route,new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json"));
            await Check(response,HttpStatusCode.BadRequest,"VAL_INVALID_INPUT"); Assert.DoesNotContain(secret,await response.Content.ReadAsStringAsync());
        }
        var oversized = await _client.PostAsync(Route,new StringContent("{\"fullName\":\"" + new string('a',17000) + "\"}",Encoding.UTF8,"application/json"));
        await Check(oversized,HttpStatusCode.RequestEntityTooLarge,"REQUEST_BODY_TOO_LARGE");
        await using var db = Db(); Assert.Empty(await db.PasswordChangeDispatches.ToListAsync());
        Assert.DoesNotContain(secret,JsonSerializer.Serialize(await db.AuditLogs.ToListAsync()));
    }

    [PostgresFact]
    public async Task Replay_NormalizesFields_IgnoresTraceAndCurrentTemplateActivation()
    {
        var request = Request(); var accepted = await Accept(request);
        await using (var db = Db())
        {
            await db.NotificationTemplates.Where(t => t.Code == "PASSWORD_CHANGED_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,false));
            await db.PasswordChangeDispatches.Where(r => r.Id == request.SourceEventId).ExecuteUpdateAsync(s => s.SetProperty(r => r.IsDeleted,true));
        }
        var replay = await Accept(request with { Email = " ACCOUNT@EXAMPLE.COM ",FullName = " <b>Customer</b> ",Kind = " changed ",
            ChangedAt = request.ChangedAt!.Value.ToOffset(TimeSpan.FromHours(7)),TraceId = "retry-other-trace" });
        Assert.True(replay.IsReplay); Assert.Equal(accepted.NotificationId,replay.NotificationId);
        await using var verify = Db(); Assert.Single(await verify.AuditLogs.Where(a => a.Action == AuditAction.PasswordChangeAccepted).ToListAsync());
    }

    [PostgresFact]
    public async Task SameEvent_ChangedTargetOrBodyConflictsInsteadOfSendingAgain()
    {
        var request = Request(); await Accept(request);
        foreach (var conflict in new[] { request with { UserId = Guid.NewGuid() },request with { Email = "other@example.com" },
            request with { FullName = "Different" },request with { Kind = "Reset" },request with { ChangedAt = request.ChangedAt!.Value.AddSeconds(-1) } })
        {
            await Check(await _client.PostAsJsonAsync(Route,conflict),HttpStatusCode.Conflict,"PASSWORD_CHANGE_CONFLICT");
        }
        await using var db = Db(); Assert.Single(await db.PasswordChangeDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentSameEvent_AcceptsOnceAndReplaysSevenTimes()
    {
        var request = Request();
        var results = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Accept(request))).WaitAsync(TimeSpan.FromSeconds(25));
        Assert.Single(results,r => !r.IsReplay); Assert.Single(results.Select(r => r.NotificationId).Distinct());
        await using var db = Db(); Assert.Single(await db.Notifications.Where(n => n.SourceEventId == request.SourceEventId).ToListAsync());
        Assert.Single(await db.NotificationLogs.Where(l => l.NotificationId == results[0].NotificationId).ToListAsync());
        Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditAction.PasswordChangeAccepted).ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentDifferentBodySameEvent_OneWinsAndOtherConflicts()
    {
        var request = Request();
        var statuses = await Task.WhenAll(new[] { request,request with { UserId = Guid.NewGuid(),Email = "other@example.com",Kind = "Reset" } }.Select(async r =>
        { try { await Accept(r); return 200; } catch (DomainException ex) { Assert.Equal("PASSWORD_CHANGE_CONFLICT",ex.ErrorCode); return ex.StatusCode; } }));
        Assert.Single(statuses,s => s == 200); Assert.Single(statuses,s => s == 409);
        await using var db = Db(); Assert.Single(await db.PasswordChangeDispatches.ToListAsync());
        Assert.Single(await db.Notifications.Where(n => n.SourceEventId == request.SourceEventId).ToListAsync());
    }

    [PostgresFact]
    public async Task AuditFailure_RollsBackPreviouslySavedDispatchAndReceipt()
    {
        var request = Request();
        await using (var db = Db(new FailSecondSave())) await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(request));
        await using var verify = Db(); Assert.Empty(await verify.PasswordChangeDispatches.ToListAsync());
        Assert.False(await verify.Notifications.IgnoreQueryFilters().AnyAsync(n => n.SourceEventId == request.SourceEventId));
        Assert.Empty(await verify.AuditLogs.Where(a => a.Action == AuditAction.PasswordChangeAccepted).ToListAsync());
        Assert.False(await Process()); Assert.False((await Accept(request)).IsReplay);
    }

    [PostgresFact]
    public async Task MandatorySecurityConfirmation_IgnoresLegacyDisabledPreference()
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
    public async Task GlobalEmailDisable_SuppressesOnce_AndReplayDoesNotRequeueAfterEnable()
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
    public async Task UnsafeBusinessTemplateConfiguration_IsRejectedAtomically()
    {
        var request = Request();
        await using var db = Db();
        foreach (var variant in new[] { 0,1,2,3 })
        {
            await db.NotificationTemplates.Where(t => t.Code == "PASSWORD_CHANGED_EMAIL").ExecuteUpdateAsync(s => s
                .SetProperty(t => t.ChannelCode,variant == 0 ? ChannelType.Push : ChannelType.Email)
                .SetProperty(t => t.NotificationType,variant == 1 ? NotificationType.Promotion : NotificationType.SecurityAlert)
                .SetProperty(t => t.IsMandatory,variant != 2).SetProperty(t => t.IsActive,variant != 3));
            await Check(await _client.PostAsJsonAsync(Route,request),variant == 3 ? HttpStatusCode.NotFound : HttpStatusCode.Conflict,
                variant == 3 ? "TEMPLATE_NOT_FOUND" : "PASSWORD_CHANGE_TEMPLATE_INVALID");
        }
        Assert.Empty(await db.PasswordChangeDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task MissingChannelAndBadPlaceholders_DoNotLeavePartialDispatch()
    {
        var request = Request();
        await using var db = Db();
        await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted,true));
        await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Conflict,"CHANNEL_NOT_FOUND");
        await db.NotificationChannels.IgnoreQueryFilters().Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted,false));
        await db.NotificationTemplates.Where(t => t.Code == "PASSWORD_CHANGED_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{Password}}"));
        await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.BadRequest,"TEMPLATE_DATA_MISSING");
        await db.NotificationTemplates.Where(t => t.Code == "PASSWORD_CHANGED_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{invalid"));
        await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Conflict,"TEMPLATE_INVALID");
        Assert.Empty(await db.PasswordChangeDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task DurableWorker_TransientRetryIsVisibleOnReplayAndDoesNotDuplicateHistory()
    {
        var request = Request(); var accepted = await Accept(request);
        _sender.Result = EmailDeliveryResult.Failure("SMTP_450","Test temporary failure.",true);
        Assert.True(await Process()); Assert.Equal("Pending",(await Accept(request)).DeliveryStatus); Assert.False(await Process());
        _clock.Advance(TimeSpan.FromSeconds(10)); _sender.Result = EmailDeliveryResult.Sent();
        Assert.True(await Process()); Assert.Equal("Sent",(await Accept(request)).DeliveryStatus);
        Assert.Equal(2,_sender.Messages.Count); Assert.Single(_sender.Messages.Select(m => m.MessageId).Distinct());
        await using var db = Db(); var delivery = await db.NotificationDeliveries.SingleAsync(d => d.Id == accepted.DeliveryId);
        Assert.Equal(2,delivery.AttemptCount); Assert.Single(await db.PasswordChangeDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task DurableWorker_PermanentProviderFailureIsNotReportedAsSent()
    {
        var request = Request(); var accepted = await Accept(request);
        _sender.Result = EmailDeliveryResult.Failure("SMTP_NOT_CONFIGURED","Test missing configuration.");
        Assert.True(await Process()); var replay = await Accept(request); Assert.Equal("Failed",replay.DeliveryStatus); Assert.False(await Process());
        await using var db = Db(); Assert.Equal("SMTP_NOT_CONFIGURED",(await db.NotificationDeliveries.SingleAsync(d => d.Id == accepted.DeliveryId)).ErrorCode);
    }

    [PostgresFact]
    public async Task ExistingGenericDispatch_CannotBeAdoptedAsVerifiedBusinessContent()
    {
        var request = Request();
        await using (var db = Db()) await new DispatchService(db).CreateAsync(new()
        {
            SourceEventId = request.SourceEventId,UserId = request.UserId,Destination = "wrong@example.com",TemplateCode = "PASSWORD_CHANGED_EMAIL",
            Data = new() { ["FullName"] = "Different",["ChangedAtUtc"] = "unknown" }
        });
        await Check(await _client.PostAsJsonAsync(Route,request),HttpStatusCode.Conflict,"PASSWORD_CHANGE_CONFLICT");
        await using var verify = Db(); Assert.Empty(await verify.PasswordChangeDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task TemplateLock_PreventsConcurrentAdminChangeBetweenValidationAndDispatch()
    {
        var request = Request(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var acceptingDb = Db();
        var gate = new GatedDispatch(new DispatchService(acceptingDb),entered,release);
        var accepting = new PasswordChangeService(acceptingDb,gate,_clock).CreateAsync(request);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var editor = Db(); await using var editTransaction = await editor.Database.BeginTransactionAsync();
            await editor.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => editor.NotificationTemplates.Where(t => t.Code == "PASSWORD_CHANGED_EMAIL")
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.NotificationType,NotificationType.Promotion)));
            Assert.Equal("55P03",Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        finally { release.TrySetResult(); }
        var accepted = await accepting.WaitAsync(TimeSpan.FromSeconds(5));
        await using var verify = Db();
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
            if (++_count == 2) throw new InvalidOperationException("Test audit write failure.");
            return ValueTask.FromResult(result);
        }
    }
}
