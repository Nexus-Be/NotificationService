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
using Notification.Application.Services.Dispatches;
using Notification.Application.Services.Notifications;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class PreferencesApiIntegrationTests : IAsyncLifetime
{
    private const string Root = "/api/v1/notifications";
    private readonly Guid _user = Guid.NewGuid();
    private string? _admin,_database,_connection;
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
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_preferences_test_" + Guid.NewGuid().ToString("N");
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
        builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddApplicationServices();
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
    private static UpdatePreferencesRequest Preference(string type = "OrderShipped",string channel = "Email",bool disabled = true)
        => new(new[] { new UpdatePreferenceItemRequest(type,channel,disabled) });
    private static async Task Check(HttpResponseMessage response,HttpStatusCode expected,string? code = null)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,$"Expected {expected}, got {response.StatusCode}: {body}");
        if (code is not null) Assert.Equal(code,JsonDocument.Parse(body).RootElement.GetProperty("errorCode").GetString());
    }
    private async Task<Guid> Notification(bool read = true,bool hidden = false,Guid? owner = null)
    {
        await using var db = Db(); var id = Guid.NewGuid();
        db.Notifications.Add(new() { Id = id,UserId = owner ?? _user,Title = "Test",Body = "Body",IsRead = read,
            ReadAt = read ? DateTimeOffset.UtcNow : null,IsDeleted = hidden,Status = NotificationStatus.Pending });
        await db.SaveChangesAsync(); return id;
    }
    private static NotificationPreference Stored(Guid user,NotificationType type,ChannelType channel,bool disabled = true,bool hidden = false)
        => new() { Id = Guid.NewGuid(),UserId = user,NotificationType = type,ChannelCode = channel,IsDisabled = disabled,IsDeleted = hidden };

    [PostgresFact]
    public async Task Defaults_AreCompleteMandatoryAwareAndReadOnly()
    {
        await using var db = Db(); var before = await db.NotificationPreferences.IgnoreQueryFilters().CountAsync(); var audits = await db.AuditLogs.CountAsync();
        var result = (await _client.GetFromJsonAsync<List<NotificationPreferenceResponse>>(Root + "/preferences"))!;
        Assert.Equal(54,result.Count); Assert.All(result,r => { Assert.Null(r.Id); Assert.True(r.IsDefault); Assert.False(r.IsDisabled); });
        Assert.Equal(18,result.Count(r => r.IsMandatory));
        Assert.Equal(before,await db.NotificationPreferences.IgnoreQueryFilters().CountAsync()); Assert.Equal(audits,await db.AuditLogs.CountAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task PreferenceUpdate_IsOwnedAuditedAndNoOpOnReplay()
    {
        await Check(await _client.PutAsJsonAsync(Root + "/preferences",Preference()),HttpStatusCode.OK);
        await using var db = Db();
        var row = await db.NotificationPreferences.AsNoTracking().SingleAsync(p => p.UserId == _user);
        var firstUpdated = row.UpdatedAt; Assert.True(row.IsDisabled);
        var replay = await _client.PutAsJsonAsync(Root + "/preferences",Preference());
        await Check(replay,HttpStatusCode.OK); Assert.Equal(0,(await replay.Content.ReadFromJsonAsync<UpdatePreferencesResult>())!.UpdatedCount);
        Assert.Equal(firstUpdated,(await db.NotificationPreferences.AsNoTracking().SingleAsync(p => p.UserId == _user)).UpdatedAt);
        Assert.Single(await db.AuditLogs.Where(a => a.UserId == _user.ToString() && a.Action == AuditAction.PreferenceUpdate).ToListAsync());
        var defaults = (await _client.GetFromJsonAsync<List<NotificationPreferenceResponse>>(Root + "/preferences"))!;
        Assert.Single(defaults,r => !r.IsDefault); Assert.Contains(defaults,r => r.Id == row.Id && r.IsDisabled);
        Identity(Guid.NewGuid()); Assert.All((await _client.GetFromJsonAsync<List<NotificationPreferenceResponse>>(Root + "/preferences"))!,r => Assert.True(r.IsDefault));
    }

    [PostgresFact]
    public async Task Validation_RejectsEntireBatchInvalidEnumsDuplicatesMissingBoolAndMandatory()
    {
        foreach (var input in new[]
        {
            Preference("0"),Preference("OrderShipped","1"),Preference("OrderShipped","Sms"),Preference("OrderShipped,OrderPaid"),
            new UpdatePreferencesRequest(new[] { new UpdatePreferenceItemRequest("OrderShipped","Email",true),new("ordershipped","email",false) }),
            new UpdatePreferencesRequest(new[] { new UpdatePreferenceItemRequest("OrderShipped","Email",true),new("invalid","Email",true) })
        }) await Check(await _client.PutAsJsonAsync(Root + "/preferences",input),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        foreach (var raw in new[] { """{}""", """{"preferences":[]}""", """{"preferences":[null]}""",
            """{"preferences":[{"notificationType":"OrderShipped","channelCode":"Email"}]}""" })
            await Check(await _client.PutAsync(Root + "/preferences",new StringContent(raw,Encoding.UTF8,"application/json")),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        foreach (var type in new[] { "OrderPaid","Otp","SecurityAlert" })
            await Check(await _client.PutAsJsonAsync(Root + "/preferences",Preference(type)),HttpStatusCode.Conflict,"PREFERENCE_MANDATORY");
        await using var db = Db(); Assert.Empty(await db.NotificationPreferences.Where(p => p.UserId == _user).ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.UserId == _user.ToString()).ToListAsync());
    }

    [PostgresFact]
    public async Task LegacyDisabledCriticalPreferences_AreEffectivelyEnabled_AndBothSendPathsBypassThem()
    {
        await using var db = Db();
        db.NotificationPreferences.AddRange(Stored(_user,NotificationType.OrderPaid,ChannelType.InApp),Stored(_user,NotificationType.OrderPaid,ChannelType.Email));
        await db.SaveChangesAsync();
        await db.NotificationTemplates.Where(t => t.NotificationType == NotificationType.OrderPaid).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory,false));
        var prefs = await new UserNotificationService(db).GetPreferencesAsync(_user);
        Assert.All(prefs.Where(p => p.NotificationType == "OrderPaid"),p => { Assert.True(p.IsMandatory); Assert.False(p.IsDisabled); });
        var dispatch = await new DispatchService(db).CreateAsync(DispatchServiceTests.Request() with { UserId = _user });
        Assert.Equal("Sent",dispatch.DeliveryStatus);
        Assert.NotNull(await new UserNotificationService(db).CreateInAppNotificationAsync(new(_user,"Critical","Body","OrderPaid")));
        await db.NotificationChannels.Where(c => c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        Assert.Null(await new UserNotificationService(db).CreateInAppNotificationAsync(new(_user,"Critical","Body","OrderPaid")));
        Assert.Equal("Skipped",(await new DispatchService(db).CreateAsync(DispatchServiceTests.Request() with { UserId = _user })).DeliveryStatus);
    }

    [PostgresFact]
    public async Task ActiveMandatoryTemplate_ProtectsPair_WhileGlobalDisabledIsReportedSeparately()
    {
        await using var db = Db();
        await db.NotificationTemplates.Where(t => t.Code == "ORDER_SHIPPED_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory,true));
        await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        await Check(await _client.PutAsJsonAsync(Root + "/preferences",Preference()),HttpStatusCode.Conflict,"PREFERENCE_MANDATORY");
        var prefs = await new UserNotificationService(db).GetPreferencesAsync(_user);
        var row = prefs.Single(p => p.NotificationType == "OrderShipped" && p.ChannelCode == "Email");
        Assert.True(row.IsMandatory); Assert.True(row.IsChannelDisabled); Assert.False(row.IsDisabled);
    }

    [PostgresFact]
    public async Task PairMandatoryPolicy_AlsoAppliesToOptionalSiblingTemplateAndLegacyStoredFlag()
    {
        await using var db = Db();
        await db.NotificationTemplates.Where(t => t.Code == "ORDER_SHIPPED_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory,true));
        db.NotificationTemplates.Add(new NotificationTemplate { Id = Guid.NewGuid(),Code = "SHIPPED_OPTIONAL_SIBLING",Locale = "vi-VN",
            NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Email,SubjectTemplate = "Order",BodyTemplate = "Body",IsActive = true,IsMandatory = false });
        db.NotificationPreferences.Add(Stored(_user,NotificationType.OrderShipped,ChannelType.Email));
        await db.SaveChangesAsync();
        var dispatch = await new DispatchService(db).CreateAsync(DispatchServiceTests.Request("SHIPPED_OPTIONAL_SIBLING") with { UserId = _user });
        Assert.Equal("Pending",dispatch.DeliveryStatus);
    }

    [PostgresFact]
    public async Task MissingChannel_Returns404WithoutPartialWrite()
    {
        await using var db = Db();
        await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted,true));
        await Check(await _client.PutAsJsonAsync(Root + "/preferences",Preference()),HttpStatusCode.NotFound,"CHANNEL_NOT_FOUND");
        Assert.Empty(await db.NotificationPreferences.Where(p => p.UserId == _user).ToListAsync());
    }

    [PostgresFact]
    public async Task ExternalWriterUniqueRace_ReturnsConflictAndRollsBackAudit()
    {
        var interceptor = new ExternalInsert(async () =>
        {
            await using var outside = Db();
            outside.NotificationPreferences.Add(Stored(_user,NotificationType.OrderShipped,ChannelType.Email,false));
            await outside.SaveChangesAsync();
        });
        await using (var db = Db(interceptor))
        {
            var error = await Assert.ThrowsAsync<DomainException>(() => new UserNotificationService(db).UpdatePreferencesAsync(_user,Preference()));
            Assert.Equal("PREFERENCE_CONFLICT",error.ErrorCode);
        }
        await using var verify = Db();
        var row = await verify.NotificationPreferences.SingleAsync(p => p.UserId == _user);
        Assert.False(row.IsDisabled);
        Assert.Empty(await verify.AuditLogs.Where(a => a.UserId == _user.ToString()).ToListAsync());
    }

    [PostgresFact]
    public async Task OptionalPreferences_AffectDispatchAndLegacyInApp_AndIgnoreHiddenValues()
    {
        await using var db = Db();
        await new UserNotificationService(db).UpdatePreferencesAsync(_user,new(new[] { new UpdatePreferenceItemRequest("OrderShipped","Email",true),new("OrderShipped","InApp",true) }));
        Assert.Equal("Skipped",(await new DispatchService(db).CreateAsync(DispatchServiceTests.Request("ORDER_SHIPPED_EMAIL") with { UserId = _user })).DeliveryStatus);
        Assert.Null(await new UserNotificationService(db).CreateInAppNotificationAsync(new(_user,"Optional","Body","OrderShipped")));
        await db.NotificationPreferences.Where(p => p.UserId == _user).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDeleted,true));
        Assert.Equal("Pending",(await new DispatchService(db).CreateAsync(DispatchServiceTests.Request("ORDER_SHIPPED_EMAIL") with { UserId = _user })).DeliveryStatus);
        Assert.NotNull(await new UserNotificationService(db).CreateInAppNotificationAsync(new(_user,"Optional","Body","OrderShipped")));
    }

    [PostgresFact]
    public async Task ConcurrentSamePreference_ProducesOneActiveRowAndOneAudit()
    {
        var results = await Task.WhenAll(Enumerable.Range(0,8).Select(async _ =>
        {
            await using var db = Db(); return await new UserNotificationService(db).UpdatePreferencesAsync(_user,Preference());
        })).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(1,results.Sum(r => r.UpdatedCount));
        await using var verify = Db();
        Assert.Single(await verify.NotificationPreferences.Where(p => p.UserId == _user).ToListAsync());
        Assert.Single(await verify.AuditLogs.Where(a => a.UserId == _user.ToString()).ToListAsync());
    }

    [PostgresFact]
    public async Task HiddenPreference_IsNotResurrected_AndUniqueIndexProtectsActiveRows()
    {
        var hidden = Stored(_user,NotificationType.OrderShipped,ChannelType.Email,hidden:true);
        await using (var db = Db()) { db.NotificationPreferences.Add(hidden); await db.SaveChangesAsync(); }
        await Check(await _client.PutAsJsonAsync(Root + "/preferences",Preference()),HttpStatusCode.OK);
        await using var verify = Db();
        Assert.Single(await verify.NotificationPreferences.Where(p => p.UserId == _user).ToListAsync());
        Assert.Equal(2,await verify.NotificationPreferences.IgnoreQueryFilters().CountAsync(p => p.UserId == _user));
        verify.NotificationPreferences.Add(Stored(_user,NotificationType.OrderShipped,ChannelType.Email));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => verify.SaveChangesAsync());
        Assert.Equal("23505",Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [PostgresFact]
    public async Task AuditSaveFailure_RollsBackBothPreferenceAndAudit()
    {
        await using (var db = Db(new FailSave()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new UserNotificationService(db).UpdatePreferencesAsync(_user,Preference()));
        await using var verify = Db();
        Assert.Empty(await verify.NotificationPreferences.Where(p => p.UserId == _user).ToListAsync());
        Assert.Empty(await verify.AuditLogs.Where(a => a.UserId == _user.ToString()).ToListAsync());
    }

    [PostgresFact]
    public async Task ReadUnread_IsOwnedIdempotentAndDoesNotChangeDeliveryState()
    {
        var id = await Notification();
        await using (var db = Db())
        {
            db.NotificationDeliveries.Add(new() { Id = Guid.NewGuid(),NotificationId = id,ChannelCode = ChannelType.Email,Destination = "test@example.com",
                Status = DeliveryStatus.Sending,AttemptCount = 2,RowVersion = 7 });
            await db.SaveChangesAsync();
        }
        await Check(await _client.PutAsJsonAsync(Root + "/" + id + "/read-states",new { isRead = false }),HttpStatusCode.OK);
        await using var verify = Db();
        var unread = await verify.Notifications.AsNoTracking().SingleAsync(n => n.Id == id);
        Assert.False(unread.IsRead); Assert.Null(unread.ReadAt); Assert.Equal(NotificationStatus.Pending,unread.Status);
        var again = await _client.PutAsJsonAsync(Root + "/" + id + "/read-states",new { isRead = false });
        await Check(again,HttpStatusCode.OK);
        Assert.Equal(unread.UpdatedAt,(await verify.Notifications.AsNoTracking().SingleAsync(n => n.Id == id)).UpdatedAt);
        await Check(await _client.PutAsJsonAsync(Root + "/" + id + "/read-states",new { isRead = true }),HttpStatusCode.OK);
        var read = await verify.Notifications.AsNoTracking().SingleAsync(n => n.Id == id); Assert.True(read.IsRead); Assert.NotNull(read.ReadAt);
        var delivery = await verify.NotificationDeliveries.AsNoTracking().SingleAsync(d => d.NotificationId == id);
        Assert.Equal(DeliveryStatus.Sending,delivery.Status); Assert.Equal(2,delivery.AttemptCount); Assert.Equal(7,delivery.RowVersion);
        var list = await new UserNotificationService(verify).GetMyNotificationsAsync(_user,new(IsRead:true));
        Assert.Contains(list.Items,n => n.Id == id);
    }

    [PostgresFact]
    public async Task ReadState_RejectsForeignDeletedMissingAndInvalidInput_WithoutAdminBypass()
    {
        var foreign = await Notification(owner:Guid.NewGuid()); var hidden = await Notification(hidden:true);
        foreach (var id in new[] { foreign,hidden,Guid.NewGuid() })
        {
            Identity(_user,"admin");
            await Check(await _client.PutAsJsonAsync(Root + "/" + id + "/read-states",new { isRead = false }),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
        }
        var own = await Notification();
        await Check(await _client.PutAsJsonAsync(Root + "/" + own + "/read-states",new { }),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await Check(await _client.PutAsJsonAsync(Root + "/" + Guid.Empty + "/read-states",new { isRead = false }),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        Identity(null);
        await Check(await _client.GetAsync(Root + "/preferences"),HttpStatusCode.Unauthorized);
        await Check(await _client.PutAsJsonAsync(Root + "/preferences",Preference()),HttpStatusCode.Unauthorized);
        await Check(await _client.PutAsJsonAsync(Root + "/" + own + "/read-states",new { isRead = false }),HttpStatusCode.Unauthorized);
    }

    [PostgresFact]
    public async Task Swagger_ShowsReadStateAndExpandedPreferenceContract()
    {
        var text = await _client.GetStringAsync("/swagger/v1/swagger.json"); using var json = JsonDocument.Parse(text);
        Assert.True(json.RootElement.GetProperty("paths").GetProperty(Root + "/{id}/read-states").TryGetProperty("put",out _));
        Assert.Contains("SetNotificationReadStateRequest",text);
    }
    private sealed class FailSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated atomic save failure.");
    }
    private sealed class ExternalInsert(Func<Task> insert) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct = default)
        {
            await insert(); return result;
        }
    }
}
