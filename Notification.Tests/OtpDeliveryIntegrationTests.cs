namespace Notification.Tests;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Notification.Api.Common;
using Notification.Api.Controllers.Public;
using Notification.Api.Middleware;
using Notification.Application;
using Notification.Application.Interfaces;
using Notification.Application.Services.Deliveries;
using Notification.Application.Services.Email;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Notification.Infrastructure.Services.Email;
using Xunit;

public sealed class OtpDeliveryIntegrationTests : IAsyncLifetime
{
    private const string PostRoute = "/api/v1/notifications/email/otp";
    private const string StatusRoute = "/api/v1/notifications/otp-dispatches/";
    private string? _admin,_database,_connection;
    private WebApplication? _app; private HttpClient _client = null!;
    private readonly Clock _clock = new();
    private readonly OtpWorkerSettings _settings = new();
    private readonly IOtpPayloadProtector _protection = new DataProtectionOtpPayloadProtector(new EphemeralDataProtectionProvider());
    private readonly FakeSender _sender = new();
    private AppDbContext Db(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }
    private OtpEmailDispatchService Service(AppDbContext db,IOtpPayloadProtector? protection = null)
        => new(db,protection ?? _protection,_sender,_settings,_clock);
    private SendOtpEmailRequest Request(string purpose = "registration") => new()
    {
        RequestId = Guid.NewGuid(),UserId = Guid.NewGuid(),Email = "otp@example.com",FullName = "<b>Test</b>",
        OtpCode = "qZ7p9S4n2X5b8V",Purpose = purpose,IssuedAt = _clock.GetUtcNow(),ExpiresAt = _clock.GetUtcNow().AddMinutes(5)
    };
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_otp_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(EmailController).Assembly)
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiValidationProblemFactory.Create);
        builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connection));
        builder.Services.AddScoped<INotificationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        builder.Services.AddSingleton<TimeProvider>(_clock); builder.Services.AddSingleton(_settings);
        builder.Services.AddSingleton(_protection); builder.Services.AddSingleton<IEmailDeliverySender>(_sender);
        builder.Services.AddSingleton(new Mock<IEmailSenderService>(MockBehavior.Strict).Object);
        builder.Services.AddApplicationServices();
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
    private async Task<OtpEmailDispatchResponse> Enqueue(SendOtpEmailRequest request)
    {
        await using var db = Db(); return await Service(db).EnqueueAsync(request);
    }
    private async Task<bool> Process(CancellationToken ct = default)
    {
        await using var db = Db(); return await Service(db).ProcessNextAsync(ct);
    }
    private async Task<OtpEmailDispatch> Row(Guid id)
    {
        await using var db = Db(); return await db.OtpEmailDispatches.IgnoreQueryFilters().AsNoTracking().SingleAsync(j => j.Id == id);
    }

    [PostgresFact]
    public async Task Http_QueuesProtectedJob_AndReturnsMaskedStatusWithNoSecretAudit()
    {
        var request = Request();
        var response = await _client.PostAsJsonAsync(PostRoute,request); await Check(response,HttpStatusCode.OK);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var accepted = (await response.Content.ReadFromJsonAsync<OtpEmailDispatchResponse>())!;
        Assert.Equal("Queued",accepted.Status); Assert.Equal(request.RequestId,accepted.RequestId); Assert.Empty(_sender.Messages);
        var row = await Row(accepted.RequestId!.Value);
        Assert.DoesNotContain(request.OtpCode,JsonSerializer.Serialize(row)); Assert.NotNull(row.ProtectedPayload);
        var status = await _client.GetAsync(StatusRoute + row.Id); await Check(status,HttpStatusCode.OK);
        Assert.True(status.Headers.CacheControl!.NoStore);
        var text = await status.Content.ReadAsStringAsync();
        Assert.Contains("***@example.com",text);
        foreach (var secret in new[] { request.OtpCode,"otp@example.com","protectedPayload","protectedFingerprint","<b>Test</b>" }) Assert.DoesNotContain(secret,text);
        Assert.True(await Process()); var sent = await Row(row.Id);
        Assert.Equal(OtpDispatchStatus.ProviderAccepted,sent.Status); Assert.Null(sent.ProtectedPayload); Assert.Equal(1,sent.AttemptCount);
        var message = Assert.Single(_sender.Messages);
        Assert.Contains("Registration",message.Subject); Assert.Contains("&lt;b&gt;Test&lt;/b&gt;",message.HtmlBody); Assert.Contains(request.OtpCode,message.HtmlBody);
        Assert.Contains("UTC",message.HtmlBody);
        await using var db = Db();
        var audits = await db.AuditLogs.Where(a => a.EntityId == row.Id.ToString()).ToListAsync();
        Assert.Equal(3,audits.Count); Assert.DoesNotContain(request.OtpCode,JsonSerializer.Serialize(audits));
        Assert.Empty(await db.Notifications.Where(n => n.NotificationType == NotificationType.Otp).ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task Http_AuthValidationAndSwagger_UseTrustedServiceContract()
    {
        var request = Request();
        Role(null); await Check(await _client.PostAsJsonAsync(PostRoute,request),HttpStatusCode.Unauthorized);
        await Check(await _client.GetAsync(StatusRoute + request.RequestId),HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "buyer","seller","staff" })
        {
            Role(role); await Check(await _client.PostAsJsonAsync(PostRoute,request),HttpStatusCode.Forbidden);
        }
        Role("admin");
        foreach (var invalid in new[] { request with { RequestId = Guid.Empty },request with { OtpCode = "" },request with { Purpose = "invalid" },
            request with { IssuedAt = null },request with { ExpiresAt = request.IssuedAt },request with { ExpiresAt = _clock.GetUtcNow().AddMinutes(61) },
            request with { IssuedAt = _clock.GetUtcNow().AddMinutes(1) },request with { Email = "invalid" } })
            await Check(await _client.PostAsJsonAsync(PostRoute,invalid),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await Check(await _client.PostAsJsonAsync(PostRoute,request with { IssuedAt = _clock.GetUtcNow().AddMinutes(-10),ExpiresAt = _clock.GetUtcNow().AddMinutes(-5) }),
            HttpStatusCode.BadRequest,"OTP_EXPIRED");
        await Check(await _client.GetAsync(StatusRoute + Guid.NewGuid()),HttpStatusCode.NotFound,"OTP_DISPATCH_NOT_FOUND");
        var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains("/api/v1/notifications/otp-dispatches/{id}",swagger);
        Assert.Contains("requestId",swagger); Assert.Contains("expiresAt",swagger);
    }

    [PostgresFact]
    public async Task Replay_AfterSendingIsStable_ConflictingCodeCannotRequeue()
    {
        var request = Request(); var first = await Enqueue(request);
        Assert.True(await Process()); var replay = await Enqueue(request with { TraceId = "different-transport-trace" });
        Assert.True(replay.IsReplay); Assert.Equal("ProviderAccepted",replay.Status); Assert.Equal(first.RequestId,replay.RequestId);
        await using var db = Db();
        var error = await Assert.ThrowsAsync<DomainException>(() => Service(db).EnqueueAsync(request with { OtpCode = "changed" }));
        Assert.Equal("OTP_REQUEST_CONFLICT",error.ErrorCode); Assert.False(await Process()); Assert.Single(_sender.Messages);
    }

    [PostgresFact]
    public async Task LegacyRelativeExpiry_ConcurrentSameIdReplaysOriginalAcceptance()
    {
        var request = Request() with { IssuedAt = null,ExpiresAt = null };
        var results = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Enqueue(request))).WaitAsync(TimeSpan.FromSeconds(25));
        Assert.Single(results,r => !r.IsReplay);
        Assert.Single(results.Select(r => r.AcceptedAt).Distinct());
        await using var db = Db(); Assert.Single(await db.OtpEmailDispatches.ToListAsync());
        Assert.Single(await db.AuditLogs.Where(a => a.EntityId == request.RequestId.ToString()).ToListAsync());
    }

    [PostgresFact]
    public async Task Resend_SupersedesOldJob_RejectsDelayedOlderRequest_AndDoesNotMixPurposes()
    {
        var old = Request(); await Enqueue(old); _clock.Advance(TimeSpan.FromSeconds(1));
        var current = Request() with { OtpCode = "new-code" }; await Enqueue(current);
        var superseded = await Row(old.RequestId!.Value); Assert.Equal(OtpDispatchStatus.Superseded,superseded.Status); Assert.Null(superseded.ProtectedPayload);
        await using (var db = Db())
            Assert.Equal("OTP_REQUEST_OBSOLETE",(await Assert.ThrowsAsync<DomainException>(() => Service(db).EnqueueAsync(old with { RequestId = Guid.NewGuid() }))).ErrorCode);
        var reset = Request("password_reset"); await Enqueue(reset);
        Assert.Equal(OtpDispatchStatus.Queued,(await Row(current.RequestId!.Value)).Status);
        await Process(); await Process();
        Assert.Equal(2,_sender.Messages.Count);
        Assert.DoesNotContain(_sender.Messages,m => m.DeliveryId == old.RequestId);
        Assert.Contains(_sender.Messages,m => m.DeliveryId == current.RequestId && m.HtmlBody.Contains("new-code"));
        Assert.Contains(_sender.Messages,m => m.Subject.Contains("Password reset"));
    }

    [PostgresFact]
    public async Task ExpiredJob_IsClearedWithoutCallingProvider()
    {
        var request = Request(); await Enqueue(request); _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await Process()); var row = await Row(request.RequestId!.Value);
        Assert.Equal(OtpDispatchStatus.Expired,row.Status); Assert.Equal(0,row.AttemptCount); Assert.Null(row.ProtectedPayload); Assert.Empty(_sender.Messages);
        Assert.True((await Enqueue(request)).IsReplay);
    }

    [PostgresFact]
    public async Task TransientFailures_BackOffAndStopAtThreeAttempts()
    {
        var request = Request(); await Enqueue(request);
        _sender.Handler = (_,_) => Task.FromResult(EmailDeliveryResult.Failure("SMTP_450","Temporary failure.",true));
        Assert.True(await Process()); Assert.False(await Process());
        _clock.Advance(TimeSpan.FromSeconds(5)); Assert.True(await Process()); Assert.False(await Process());
        _clock.Advance(TimeSpan.FromSeconds(10)); Assert.True(await Process()); Assert.False(await Process());
        var row = await Row(request.RequestId!.Value);
        Assert.Equal(OtpDispatchStatus.Failed,row.Status); Assert.Equal(3,row.AttemptCount); Assert.Null(row.ProtectedPayload);
        Assert.Equal(3,_sender.Messages.Count);
        Assert.Single(_sender.Messages.Select(m => m.MessageId).Distinct());
    }

    [PostgresFact]
    public async Task PermanentFailureAndInsufficientRetryWindow_DoNotQueueForever()
    {
        var first = Request(); await Enqueue(first);
        _sender.Handler = (_,_) => Task.FromResult(EmailDeliveryResult.Failure("SMTP_NOT_CONFIGURED","Missing configuration."));
        await Process(); Assert.Equal(OtpDispatchStatus.Failed,(await Row(first.RequestId!.Value)).Status);
        _clock.Advance(TimeSpan.FromSeconds(1));
        var shortRequest = Request() with { ExpiresAt = _clock.GetUtcNow().AddSeconds(3) }; await Enqueue(shortRequest);
        _sender.Handler = (_,_) => Task.FromResult(EmailDeliveryResult.Failure("SMTP_450","Temporary failure.",true));
        await Process(); Assert.Equal(OtpDispatchStatus.Expired,(await Row(shortRequest.RequestId!.Value)).Status);
    }

    [PostgresFact]
    public async Task WorkerRestart_RecoversSendingAfterLease_WithoutLosingEncryptedPayload()
    {
        var request = Request(); await Enqueue(request); using var stop = new CancellationTokenSource();
        _sender.Handler = (_,ct) => { stop.Cancel(); return Task.FromCanceled<EmailDeliveryResult>(ct); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Process(stop.Token));
        var row = await Row(request.RequestId!.Value); Assert.Equal(OtpDispatchStatus.Sending,row.Status); Assert.NotNull(row.ProtectedPayload);
        _sender.Handler = (_,_) => Task.FromResult(EmailDeliveryResult.Sent());
        Assert.False(await Process()); _clock.Advance(TimeSpan.FromSeconds(121)); Assert.True(await Process());
        Assert.Equal(2,(await Row(row.Id)).AttemptCount); Assert.Equal(OtpDispatchStatus.ProviderAccepted,(await Row(row.Id)).Status);
    }

    [PostgresFact]
    public async Task LateResult_AfterNewChallengeCannotOverwriteSupersededJob()
    {
        var old = Request(); await Enqueue(old);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sender.Handler = async (_,ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return EmailDeliveryResult.Sent(); };
        var processing = Process(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _clock.Advance(TimeSpan.FromSeconds(1)); var current = Request(); await Enqueue(current);
        release.TrySetResult(); await processing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(OtpDispatchStatus.Superseded,(await Row(old.RequestId!.Value)).Status);
        Assert.Equal(OtpDispatchStatus.Queued,(await Row(current.RequestId!.Value)).Status);
    }

    [PostgresFact]
    public async Task LeaseReclaim_FencesAnOldWorkersFailure()
    {
        var request = Request(); await Enqueue(request);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        _sender.Handler = async (_,ct) =>
        {
            if (Interlocked.Increment(ref calls) == 1) { entered.TrySetResult(); await release.Task.WaitAsync(ct); return EmailDeliveryResult.Failure("SMTP_450","Old failure.",true); }
            return EmailDeliveryResult.Sent();
        };
        var first = Process(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); _clock.Advance(TimeSpan.FromSeconds(121));
        Assert.True(await Process()); release.TrySetResult(); await first.WaitAsync(TimeSpan.FromSeconds(5));
        var row = await Row(request.RequestId!.Value); Assert.Equal(OtpDispatchStatus.ProviderAccepted,row.Status); Assert.Null(row.ErrorCode);
        Assert.Equal(2,row.AttemptCount);
    }

    [PostgresFact]
    public async Task EnqueueAuditFailure_RollsBackSupersessionAndNewRequest()
    {
        var old = Request(); await Enqueue(old); _clock.Advance(TimeSpan.FromSeconds(1)); var current = Request();
        await using (var db = Db(new FailSave()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).EnqueueAsync(current));
        Assert.Equal(OtpDispatchStatus.Queued,(await Row(old.RequestId!.Value)).Status);
        await using var verify = Db(); Assert.False(await verify.OtpEmailDispatches.AnyAsync(j => j.Id == current.RequestId));
        Assert.Single(await verify.AuditLogs.Where(a => a.EntityId == old.RequestId.ToString()).ToListAsync());
    }

    [PostgresFact]
    public async Task UnreadableProtection_FailsClosedWithoutSecretOrProviderCall()
    {
        var request = Request(); await Enqueue(request);
        var wrong = new DataProtectionOtpPayloadProtector(new EphemeralDataProtectionProvider());
        await using var db = Db();
        Assert.Equal("OTP_PROTECTION_UNAVAILABLE",(await Assert.ThrowsAsync<DomainException>(() => Service(db,wrong).EnqueueAsync(request))).ErrorCode);
        Assert.True(await Service(db,wrong).ProcessNextAsync());
        var row = await Row(request.RequestId!.Value);
        Assert.Equal("OTP_PAYLOAD_UNAVAILABLE",row.ErrorCode); Assert.Null(row.ProtectedPayload); Assert.Empty(_sender.Messages);
    }

    [PostgresFact]
    public async Task FinalizationAuditFailure_RollsBackResultAndSecretErasure()
    {
        var request = Request(); await Enqueue(request);
        await using (var db = Db(new FailSecondSave()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).ProcessNextAsync());
        var row = await Row(request.RequestId!.Value); Assert.Equal(OtpDispatchStatus.Sending,row.Status); Assert.NotNull(row.ProtectedPayload);
        _clock.Advance(TimeSpan.FromSeconds(121)); Assert.True(await Process());
        Assert.Equal(OtpDispatchStatus.ProviderAccepted,(await Row(row.Id)).Status);
    }

    [PostgresFact]
    public async Task CrossGroupSameIdRace_RollsBackLosingAuditAndReturnsConflict()
    {
        var request = Request(); var barrier = new SaveBarrier();
        var statuses = await Task.WhenAll(new[] { request,request with { Email = "other@example.com" } }.Select(async r =>
        {
            await using var db = Db(barrier);
            try { await Service(db).EnqueueAsync(r); return 200; }
            catch (DomainException ex) { Assert.Equal("OTP_REQUEST_CONFLICT",ex.ErrorCode); return ex.StatusCode; }
        })).WaitAsync(TimeSpan.FromSeconds(25));
        Assert.Single(statuses,s => s == 200); Assert.Single(statuses,s => s == 409);
        await using var verify = Db();
        Assert.Single(await verify.OtpEmailDispatches.ToListAsync());
        Assert.Single(await verify.AuditLogs.Where(a => a.EntityId == request.RequestId.ToString()).ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentWorkers_ClaimOnlyOneLiveAttempt()
    {
        var request = Request(); await Enqueue(request);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sender.Handler = async (_,ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return EmailDeliveryResult.Sent(); };
        var first = Process(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Process())).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(_sender.Messages); release.TrySetResult(); await first;
        Assert.Equal(1,(await Row(request.RequestId!.Value)).AttemptCount);
    }

    [PostgresFact]
    public async Task ProviderTimeout_KeepsProtectedPayloadForBoundedRetry()
    {
        _settings.SendTimeoutSeconds = 1;
        var request = Request(); await Enqueue(request);
        _sender.Handler = async (_,ct) => { await Task.Delay(Timeout.InfiniteTimeSpan,ct); return EmailDeliveryResult.Sent(); };
        Assert.True(await Process().WaitAsync(TimeSpan.FromSeconds(5)));
        var row = await Row(request.RequestId!.Value);
        Assert.Equal(OtpDispatchStatus.Queued,row.Status); Assert.Equal("OTP_SEND_TIMEOUT",row.ErrorCode);
        Assert.Equal(1,row.AttemptCount); Assert.NotNull(row.ProtectedPayload); Assert.False(await Process());
    }

    [Fact]
    public void PersistedKeyRing_CanDecryptAcrossProviderInstances_AndSeparatesPurposes()
    {
        var directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(),"notification-otp-keys-test-" + Guid.NewGuid().ToString("N")));
        directory.Create();
        try
        {
            var first = new DataProtectionOtpPayloadProtector(DataProtectionProvider.Create(directory,b => b.SetApplicationName("Nexus.Notification.Otp")));
            var payload = first.ProtectPayload("test secret"); var fingerprint = first.ProtectFingerprint("test fingerprint");
            var restarted = new DataProtectionOtpPayloadProtector(DataProtectionProvider.Create(directory,b => b.SetApplicationName("Nexus.Notification.Otp")));
            Assert.Equal("test secret",restarted.UnprotectPayload(payload));
            Assert.Equal("test fingerprint",restarted.UnprotectFingerprint(fingerprint));
            Assert.Throws<System.Security.Cryptography.CryptographicException>(() => restarted.UnprotectFingerprint(payload));
        }
        finally { foreach (var file in directory.GetFiles()) file.Delete(); directory.Delete(); }
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(DateTimeOffset.UtcNow.Ticks / 10 * 10,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
    private sealed class FakeSender : IEmailDeliverySender
    {
        public ConcurrentQueue<EmailDeliveryMessage> Messages { get; } = new();
        public Func<EmailDeliveryMessage,CancellationToken,Task<EmailDeliveryResult>> Handler { get; set; } = (_,_) => Task.FromResult(EmailDeliveryResult.Sent());
        public Task<EmailDeliveryResult> SendAsync(EmailDeliveryMessage message,CancellationToken ct = default) { Messages.Enqueue(message); return Handler(message,ct); }
    }
    private sealed class FailSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated audit failure.");
    }
    private sealed class FailSecondSave : SaveChangesInterceptor
    {
        private int _count;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct = default)
        {
            if (++_count == 2) throw new InvalidOperationException("Simulated finalization failure.");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private int _count;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _count) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(15),ct); return result;
        }
    }
}
