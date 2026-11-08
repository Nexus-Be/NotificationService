namespace Notification.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
using Moq;
using Npgsql;
using Notification.Api.Common;
using Notification.Api.Controllers.Public;
using Notification.Api.Middleware;
using Notification.Application;
using Notification.Application.Interfaces;
using Notification.Application.Services.Deliveries;
using Notification.Application.Services.Dispatches;
using Notification.Application.Services.Notifications;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class DeviceRegistrationApiTests : IAsyncLifetime
{
    private const string Route = "/api/v1/notifications/devices";
    private readonly Guid _user = Guid.NewGuid();
    private string? _admin,_database,_connection;
    private WebApplication? _app;
    private HttpClient _client = null!;
    private readonly Clock _clock = new();
    private AppDbContext Db(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_device_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(NotificationsController).Assembly)
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiValidationProblemFactory.Create);
        builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connection));
        builder.Services.AddScoped<INotificationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        builder.Services.AddSingleton<TimeProvider>(_clock); builder.Services.AddApplicationServices();
        _app = builder.Build(); _app.UseGlobalExceptionHandler(); _app.UseMiddleware<UserContextMiddleware>(); _app.UseSwagger(); _app.MapControllers();
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        Identity(_user);
    }
    public async Task DisposeAsync()
    {
        _client?.Dispose(); if (_app is not null) await _app.DisposeAsync();
        if (_database is null || _admin is null) return;
        NpgsqlConnection.ClearAllPools(); await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("DROP DATABASE \"" + _database + "\" WITH (FORCE)",admin).ExecuteNonQueryAsync();
    }
    private void Identity(Guid? user,string role = "buyer")
    {
        _client.DefaultRequestHeaders.Remove("x-user-id"); _client.DefaultRequestHeaders.Remove("x-user-roles");
        if (user.HasValue) { _client.DefaultRequestHeaders.Add("x-user-id",user.ToString()); _client.DefaultRequestHeaders.Add("x-user-roles",role); }
    }
    private static string Token() => "private-registration-" + Guid.NewGuid().ToString("N");
    private static async Task Check(HttpResponseMessage response,HttpStatusCode expected,string? code = null)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,$"Expected {expected}, got {response.StatusCode}: {body}");
        if (code is not null) Assert.Equal(code,JsonDocument.Parse(body).RootElement.GetProperty("errorCode").GetString());
    }
    private async Task<DeviceToken> Register(string token,string platform = "Android",string? previous = null)
    {
        var response = await _client.PostAsJsonAsync(Route,new RegisterDeviceRequest(token,platform,previous));
        await Check(response,HttpStatusCode.OK); Assert.True(response.Headers.CacheControl!.NoStore);
        await using var db = Db(); return await db.DeviceTokens.AsNoTracking().SingleAsync(d => d.Token == token.Trim());
    }
    private UserNotificationService Service(AppDbContext db) => new(db,_clock);

    [PostgresFact]
    public async Task RegistrationRefresh_IsStableIdAndMonotonic_EvenWithRepeatedClock()
    {
        var token = Token(); var first = await Register(token);
        var next = await Register(token,"iOS");
        Assert.Equal(first.Id,next.Id); Assert.Equal(first.CreatedAt,next.CreatedAt);
        Assert.Equal(DevicePlatform.Ios,next.Platform); Assert.True(next.UpdatedAt > first.UpdatedAt); Assert.Equal(next.UpdatedAt,next.LastSeenAt);
        await using var db = Db();
        var audits = await db.AuditLogs.Where(a => a.UserId == _user.ToString()).ToListAsync(); Assert.Equal(2,audits.Count);
        var serialized = JsonSerializer.Serialize(audits);
        Assert.DoesNotContain(token,serialized);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),serialized);
        Assert.All(audits,a => { Assert.Equal(AuditAction.DeviceRegister,a.Action); Assert.False(a.IsSynced); });
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task Validation_RejectsBadTokensPlatformsAndRotation_BeforeWrites()
    {
        foreach (var body in new[]
        {
            """{}""", """{"token":null}""", """{"token":""}""", """{"token":"valid","platform":null}""",
            """{"token":"valid","platform":"1"}""", """{"token":"valid","platform":"Web,Android"}""",
            """{"token":"with space","platform":"Web"}""", """{"token":"bad\u0000token"}""",
            """{"token":"valid","previousToken":""}""", """{"token":"same","previousToken":" same "}"""
        }) await Check(await _client.PostAsync(Route,new StringContent(body,Encoding.UTF8,"application/json")),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await Check(await _client.PostAsJsonAsync(Route,new RegisterDeviceRequest(new string('x',1001))),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await Check(await _client.PostAsJsonAsync(Route,new RegisterDeviceRequest("valid","Android",new string('x',1001))),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await using var db = Db(); Assert.Empty(await db.DeviceTokens.Where(d => d.UserId == _user).ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.UserId == _user.ToString()).ToListAsync());
    }

    [PostgresFact]
    public async Task DefaultPlatformAndAuthentication_AreEnforcedThroughHttp()
    {
        var token = Token();
        Identity(null); await Check(await _client.PostAsJsonAsync(Route,new { token }),HttpStatusCode.Unauthorized);
        Identity(_user);
        var response = await _client.PostAsJsonAsync(Route,new { token = " " + token + " " });
        await Check(response,HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<DeviceRegisteredResponse>())!;
        Assert.Equal("Web",result.Platform); Assert.Equal(token,result.Token);
    }

    [PostgresFact]
    public async Task ActiveOwnershipCannotBeStolen_RevocationPermitsAuditedAccountTransfer()
    {
        var token = Token(); var first = await Register(token);
        var other = Guid.NewGuid(); Identity(other,"admin");
        await Check(await _client.PostAsJsonAsync(Route,new RegisterDeviceRequest(token)),HttpStatusCode.Conflict,"DEVICE_TOKEN_OWNERSHIP_CONFLICT");
        await Check(await _client.DeleteAsync(Route + "/" + Uri.EscapeDataString(token)),HttpStatusCode.NoContent);
        await using (var db = Db()) Assert.True((await db.DeviceTokens.AsNoTracking().SingleAsync(d => d.Id == first.Id)).IsActive);
        Identity(_user); await Check(await _client.DeleteAsync(Route + "/" + token),HttpStatusCode.NoContent);
        Identity(other); var moved = await Register(token);
        Assert.Equal(first.Id,moved.Id); Assert.Equal(other,moved.UserId);
        Identity(_user); await Check(await _client.DeleteAsync(Route + "/" + token),HttpStatusCode.NoContent);
        await using var verify = Db(); Assert.True((await verify.DeviceTokens.AsNoTracking().SingleAsync(d => d.Id == first.Id)).IsActive);
        var audit = await verify.AuditLogs.Where(a => a.UserId == other.ToString() && a.Action == AuditAction.DeviceRegister).SingleAsync();
        Assert.Contains("Transferred",audit.NewValues); Assert.DoesNotContain(token,audit.NewValues!);
    }

    [PostgresFact]
    public async Task Rotation_ReplacesOldOwnedRegistrationAndRevokeNoOpDoesNotAddAudit()
    {
        var old = await Register(Token()); var token = Token(); var current = await Register(token,"Web",old.Token);
        await using var db = Db();
        Assert.False((await db.DeviceTokens.AsNoTracking().SingleAsync(d => d.Id == old.Id)).IsActive); Assert.True(current.IsActive);
        var count = await db.AuditLogs.CountAsync(a => a.UserId == _user.ToString());
        Assert.Equal(3,count);
        await Check(await _client.DeleteAsync(Route + "/" + old.Token),HttpStatusCode.NoContent);
        Assert.Equal(count,await db.AuditLogs.CountAsync(a => a.UserId == _user.ToString()));
        Assert.Equal(current.Id,(await Register(token,"Web",old.Token)).Id);
    }

    [PostgresFact]
    public async Task HiddenOrForeignPreviousTokens_AreRejectedWithoutPartialRegistration()
    {
        var old = await Register(Token());
        Identity(Guid.NewGuid()); await Check(await _client.PostAsJsonAsync(Route,new RegisterDeviceRequest(Token(),"Web",old.Token)),HttpStatusCode.NotFound,"DEVICE_TOKEN_NOT_FOUND");
        Identity(_user);
        await using var db = Db(); await db.DeviceTokens.Where(d => d.Id == old.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsDeleted,true));
        await Check(await _client.PostAsJsonAsync(Route,new RegisterDeviceRequest(old.Token)),HttpStatusCode.Conflict,"DEVICE_TOKEN_UNAVAILABLE");
        await Check(await _client.PostAsJsonAsync(Route,new RegisterDeviceRequest(Token(),"Web",old.Token)),HttpStatusCode.NotFound,"DEVICE_TOKEN_NOT_FOUND");
        await Check(await _client.PostAsJsonAsync(Route,new RegisterDeviceRequest(Token(),"Web",Token())),HttpStatusCode.NotFound,"DEVICE_TOKEN_NOT_FOUND");
        Assert.Single(await db.DeviceTokens.IgnoreQueryFilters().Where(d => d.UserId == _user).ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentRefresh_CreatesOneRowAndOneHeartbeatAuditPerAcceptedPost()
    {
        var token = Token();
        var results = await Task.WhenAll(Enumerable.Range(0,8).Select(async _ =>
        {
            await using var db = Db(); return await Service(db).RegisterDeviceAsync(_user,new(token));
        })).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(8,results.Select(r => r.RegisteredAt).Distinct().Count());
        await using var verify = Db();
        Assert.Single(await verify.DeviceTokens.Where(d => d.Token == token).ToListAsync());
        Assert.Equal(8,await verify.AuditLogs.CountAsync(a => a.UserId == _user.ToString() && a.Action == AuditAction.DeviceRegister));
    }

    [PostgresFact]
    public async Task RotationSaveFailure_RollsBackNewTokenOldRevokeAndAudits()
    {
        var old = await Register(Token()); var token = Token();
        await using (var db = Db(new FailSave()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).RegisterDeviceAsync(_user,new(token,"Web",old.Token)));
        await using var verify = Db();
        Assert.True((await verify.DeviceTokens.AsNoTracking().SingleAsync(d => d.Id == old.Id)).IsActive);
        Assert.False(await verify.DeviceTokens.AnyAsync(d => d.Token == token));
        Assert.Single(await verify.AuditLogs.Where(a => a.UserId == _user.ToString()).ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentRevokeAndAccountTransfer_CannotRevokeTheNewOwner()
    {
        var token = Token(); var original = await Register(token); var other = Guid.NewGuid();
        var revoke = Task.Run(async () => { await using var db = Db(); await Service(db).RevokeDeviceAsync(_user,token); });
        var transfer = Task.Run(async () =>
        {
            await using var db = Db();
            try { await Service(db).RegisterDeviceAsync(other,new(token)); return true; }
            catch (DomainException ex) { Assert.Equal("DEVICE_TOKEN_OWNERSHIP_CONFLICT",ex.ErrorCode); return false; }
        });
        await Task.WhenAll(revoke,transfer).WaitAsync(TimeSpan.FromSeconds(15));
        await using var verify = Db(); var row = await verify.DeviceTokens.AsNoTracking().SingleAsync(d => d.Id == original.Id);
        if (await transfer) { Assert.Equal(other,row.UserId); Assert.True(row.IsActive); }
        else { Assert.Equal(_user,row.UserId); Assert.False(row.IsActive); }
    }

    [PostgresFact]
    public async Task OppositeConcurrentRotations_UseConsistentLockOrder()
    {
        var a = await Register(Token()); var b = await Register(Token());
        await Task.WhenAll(new[] { (a.Token,b.Token),(b.Token,a.Token) }.Select(async pair =>
        {
            await using var db = Db(); await Service(db).RegisterDeviceAsync(_user,new(pair.Item1,"Web",pair.Item2));
        })).WaitAsync(TimeSpan.FromSeconds(15));
        await using var verify = Db();
        Assert.Equal(2,await verify.DeviceTokens.CountAsync(d => d.UserId == _user));
        Assert.Equal(1,await verify.DeviceTokens.CountAsync(d => d.UserId == _user && d.IsActive));
        Assert.Equal(6,await verify.AuditLogs.CountAsync(audit => audit.UserId == _user.ToString()));
    }

    [PostgresFact]
    public async Task OutsideUniqueWriter_Returns409AndRollsBackRotation()
    {
        var old = await Register(Token()); var token = Token(); var outsider = Guid.NewGuid();
        var interceptor = new ExternalInsert(async () =>
        {
            await using var external = Db();
            external.DeviceTokens.Add(new() { Id = Guid.NewGuid(),UserId = outsider,Token = token,Platform = DevicePlatform.Web });
            await external.SaveChangesAsync();
        });
        await using (var db = Db(interceptor))
        {
            var error = await Assert.ThrowsAsync<DomainException>(() => Service(db).RegisterDeviceAsync(_user,new(token,"Web",old.Token)));
            Assert.Equal("DEVICE_TOKEN_CONFLICT",error.ErrorCode);
        }
        await using var verify = Db();
        Assert.True((await verify.DeviceTokens.AsNoTracking().SingleAsync(d => d.Id == old.Id)).IsActive);
        Assert.Equal(outsider,(await verify.DeviceTokens.AsNoTracking().SingleAsync(d => d.Token == token)).UserId);
        Assert.Single(await verify.AuditLogs.Where(a => a.UserId == _user.ToString()).ToListAsync());
    }

    [PostgresFact]
    public async Task RegistrationRefresh_FencesAnOldUnregisteredFcmResult_EvenAtSameClock()
    {
        var device = await Register(Token());
        await using (var db = Db()) await new DispatchService(db).CreateAsync(DispatchServiceTests.Request("ORDER_PAID_PUSH") with { UserId = _user,Destination = null });
        var push = new Mock<IPushDeliverySender>();
        push.Setup(p => p.SendAsync(It.IsAny<PushDeliveryMessage>(),It.IsAny<CancellationToken>())).Returns(async (PushDeliveryMessage _,CancellationToken ct) =>
        {
            await using var refresh = Db(); await Service(refresh).RegisterDeviceAsync(_user,new(device.Token,"Android"),ct);
            return PushDeliveryResult.Failure("FCM_UNREGISTERED","Old registration failed.",invalidateToken:true);
        });
        var email = new Mock<IEmailDeliverySender>(MockBehavior.Strict);
        await using (var db = Db()) Assert.True(await new DeliveryService(db,email.Object,new(),_clock,push.Object).ProcessNextAsync());
        await using var verify = Db();
        var current = await verify.DeviceTokens.AsNoTracking().SingleAsync(d => d.Id == device.Id);
        Assert.True(current.IsActive); Assert.True(current.UpdatedAt > device.UpdatedAt);
        Assert.Empty(await verify.AuditLogs.Where(a => a.UserId == _user.ToString() && a.Action == AuditAction.DeviceRevoke).ToListAsync());
        email.VerifyNoOtherCalls();
    }

    [PostgresFact]
    public async Task PendingOldDeviceDelivery_IsSkippedAfterRotation()
    {
        var old = await Register(Token());
        Guid deliveryId;
        await using (var db = Db())
            deliveryId = (await new DispatchService(db).CreateAsync(DispatchServiceTests.Request("ORDER_PAID_PUSH") with { UserId = _user,Destination = null })).DeliveryId;
        await Register(Token(),"Android",old.Token);
        var push = new Mock<IPushDeliverySender>(MockBehavior.Strict); var email = new Mock<IEmailDeliverySender>(MockBehavior.Strict);
        await using (var db = Db()) Assert.True(await new DeliveryService(db,email.Object,new(),_clock,push.Object).ProcessNextAsync());
        await using var verify = Db();
        Assert.Equal(DeliveryStatus.Skipped,(await verify.NotificationDeliveries.SingleAsync(d => d.Id == deliveryId)).Status);
        push.VerifyNoOtherCalls(); email.VerifyNoOtherCalls();
    }

    [PostgresFact]
    public async Task BodyLimitAndSwagger_AdvertiseValidatedRotationContract()
    {
        await Check(await _client.PostAsync(Route,new StringContent("{\"token\":\"" + new string('x',17000) + "\"}",Encoding.UTF8,"application/json")),
            HttpStatusCode.RequestEntityTooLarge,"REQUEST_BODY_TOO_LARGE");
        var text = await _client.GetStringAsync("/swagger/v1/swagger.json"); using var json = JsonDocument.Parse(text);
        var schema = json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("RegisterDeviceRequest");
        Assert.True(schema.GetProperty("properties").TryGetProperty("previousToken",out _));
        Assert.Equal(1000,schema.GetProperty("properties").GetProperty("token").GetProperty("maxLength").GetInt32());
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-11-08T00:00:00Z");
    }
    private sealed class FailSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated audit persistence failure.");
    }
    private sealed class ExternalInsert(Func<Task> insert) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct = default)
        {
            await insert(); return result;
        }
    }
}
