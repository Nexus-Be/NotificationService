namespace Notification.Tests;

using Notification.Application.Services.Notifications;
using Notification.Application.Common.Models;
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
using Notification.Application.Services.AuctionStarts;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class AuctionStartApiIntegrationTests : IAsyncLifetime
{
    private const string Route = "/api/v1/notifications/auction-starts";
    private string? _admin,_database,_connection;
    private WebApplication? _app;
    private HttpClient _client = null!;
    private readonly Clock _clock = new();
    private readonly FakeSender _sender = new();
    private readonly FakePush _push = new();
    private AppDbContext Db(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }
    private AuctionStartService Service(AppDbContext db) => new(db,_clock);
    private CreateAuctionStartRequest Request() => new()
    {
        SourceEventId = Guid.NewGuid(),AuctionId = Guid.NewGuid(),SellerUserId = Guid.NewGuid(),AuctionTitle = "<b>Product</b>",AuctionStatus = "Active",
        ScheduledStartAt = _clock.GetUtcNow().AddMinutes(-2),StartedAt = _clock.GetUtcNow().AddMinutes(-1),AuctionEndsAt = _clock.GetUtcNow().AddHours(2)
    };
    private async Task<AuctionStartResponse> Accept(CreateAuctionStartRequest request)
    { await using var db = Db(); return await Service(db).CreateAsync(request); }
    private async Task<bool> Process()
    { await using var db = Db(); return await new DeliveryService(db,_sender,new(),_clock,_push).ProcessNextAsync(); }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_auction_start_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(AuctionStartsController).Assembly)
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
    private void Role(string? role,Guid? userId = null)
    {
        _client.DefaultRequestHeaders.Remove("x-user-id"); _client.DefaultRequestHeaders.Remove("x-user-roles");
        if (role is null) return;
        _client.DefaultRequestHeaders.Add("x-user-id",(userId ?? Guid.NewGuid()).ToString()); _client.DefaultRequestHeaders.Add("x-user-roles",role);
    }
    private static async Task Check(HttpResponseMessage response,HttpStatusCode expected,string? code = null)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,$"Expected {expected}, got {response.StatusCode}: {text}");
        if (code is not null) Assert.Equal(code,JsonDocument.Parse(text).RootElement.GetProperty("errorCode").GetString());
    }

    [PostgresFact]
    public async Task Activation_NotifiesOnlySeller_OneLogicalItemAndNoOtherBusinessClaims()
    {
        var r = Request(); await Device(r.SellerUserId!.Value,2); var other = Guid.NewGuid(); await Device(other);
        var response = await _client.PostAsJsonAsync(Route,r); await Check(response,HttpStatusCode.OK); Assert.True(response.Headers.CacheControl!.NoStore);
        var accepted = (await response.Content.ReadFromJsonAsync<AuctionStartResponse>())!; Assert.Equal(r.SellerUserId,accepted.UserId); Assert.Equal(3,accepted.Deliveries.Count); Assert.DoesNotContain(accepted.Deliveries,d => d.Channel == "Email");
        Assert.True(await Process()); Assert.True(await Process()); Assert.False(await Process()); Assert.Empty(_sender.Messages); Assert.Equal(2,_push.Messages.Count);
        Assert.All(_push.Messages,m => { Assert.Equal(r.SellerUserId,m.UserId); Assert.Equal(accepted.NotificationId,m.NotificationId); Assert.Contains("được kích hoạt lúc",m.Body); Assert.Contains("tại lần kích hoạt",m.Body); Assert.DoesNotContain("đang diễn ra",m.Body); Assert.DoesNotContain("giao hàng",m.Body); });
        var item = Assert.Single((await Inbox(r.SellerUserId.Value)).Items); Assert.Empty((await Inbox(other)).Items); Assert.Contains("<b>Product</b>",item.Body); Assert.DoesNotContain(r.SellerUserId.ToString()!,item.Payload);
        using var payload = JsonDocument.Parse(item.Payload!); Assert.Equal("AuctionStarted",payload.RootElement.GetProperty("type").GetString()); Assert.Equal("Seller",payload.RootElement.GetProperty("audience").GetString()); Assert.Equal("Active",payload.RootElement.GetProperty("auctionStatus").GetString());
        await using var db = Db(); Assert.Single(await db.AuctionStartDispatches.ToListAsync()); Assert.False(db.Database.HasPendingModelChanges()); var audit = Assert.Single(await db.AuditLogs.Where(x => x.Action == AuditAction.AuctionStartAccepted).ToListAsync()); Assert.DoesNotContain(r.AuctionTitle,audit.NewValues);
    }

    [PostgresFact]
    public async Task InvalidActivationStatesAndTimes_AreNotInferredFromSchedule()
    {
        var r = Request();
        foreach(var invalid in new[] { r with { AuctionStatus = "Scheduled" },r with { AuctionStatus = "Cancelled" },r with { AuctionStatus = "Failed" },r with { AuctionStatus = "Running" },r with { ScheduledStartAt = r.StartedAt!.Value.AddSeconds(1) },r with { StartedAt = _clock.GetUtcNow().AddSeconds(1) },r with { AuctionEndsAt = r.StartedAt },r with { ScheduledStartAt = null },r with { StartedAt = null },r with { AuctionEndsAt = null } })
            await Check(await _client.PostAsJsonAsync(Route,invalid),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await using var db = Db(); Assert.Empty(await db.AuctionStartDispatches.ToListAsync());
        await Check(await _client.PostAsJsonAsync(Route,r with { ScheduledStartAt = _clock.GetUtcNow(),StartedAt = _clock.GetUtcNow() }),HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task StrictIdsAndUnknownPrivateOrBusinessFields_AreRejected()
    {
        var r = Request();
        foreach(var invalid in new[] { r with { SourceEventId = Guid.Empty },r with { AuctionId = Guid.Empty },r with { SellerUserId = Guid.Empty },r with { AuctionTitle = "\n" },r with { TraceId = "\n" } }) await Check(await _client.PostAsJsonAsync(Route,invalid),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        foreach(var field in new[] { "winnerUserId","bidId","orderId","winningAmount","email","phone","paymentUrl","stockReserved","password","otpCode","token" })
        { var json = JsonSerializer.Serialize(r); json=json[..^1]+",\""+field+"\":\"private\"}"; await Check(await _client.PostAsync(Route,new StringContent(json,Encoding.UTF8,"application/json")),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT"); }
        await using var db = Db(); Assert.Empty(await db.AuctionStartDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task HistoricalActivationReplay_PreservesOriginalScheduleEndAndDevices()
    {
        var r = Request() with { ScheduledStartAt = _clock.GetUtcNow().AddDays(-3),StartedAt = _clock.GetUtcNow().AddDays(-3),AuctionEndsAt = _clock.GetUtcNow().AddDays(-2) }; await Device(r.SellerUserId!.Value); var first = await Accept(r); await Device(r.SellerUserId.Value);
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_STARTED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,false));
        var replay = await Accept(r with { TraceId = "retry",AuctionTitle = " "+r.AuctionTitle+" ",AuctionStatus = "active",ScheduledStartAt = r.ScheduledStartAt!.Value.ToOffset(TimeSpan.FromHours(7)),StartedAt = r.StartedAt!.Value.ToOffset(TimeSpan.FromHours(7)),AuctionEndsAt = r.AuctionEndsAt!.Value.ToOffset(TimeSpan.FromHours(7)) });
        Assert.True(replay.IsReplay); Assert.Equal(first.NotificationId,replay.NotificationId); Assert.Equal(first.Deliveries.Select(d => d.Id),replay.Deliveries.Select(d => d.Id));
        var item = Assert.Single((await Inbox(r.SellerUserId.Value)).Items); using var payload = JsonDocument.Parse(item.Payload!); Assert.Equal(r.AuctionEndsAt.Value.UtcTicks/10*10,payload.RootElement.GetProperty("auctionEndsAt").GetDateTimeOffset().UtcTicks);
    }

    [PostgresFact]
    public async Task ProofChangesAndHiddenAuctionAlias_ConflictButNewAuctionAllowsSameSeller()
    {
        var r = Request(); var accepted = await Accept(r);
        foreach(var changed in new[] { r with { SellerUserId = Guid.NewGuid() },r with { AuctionTitle = "Changed" },r with { AuctionEndsAt = r.AuctionEndsAt!.Value.AddMinutes(1) },r with { StartedAt = r.StartedAt!.Value.AddSeconds(1) },r with { ScheduledStartAt = r.ScheduledStartAt!.Value.AddSeconds(1) } })
        { var error=await Assert.ThrowsAsync<DomainException>(() => Accept(changed)); Assert.Equal("AUCTION_START_CONFLICT",error.ErrorCode); }
        await using(var db = Db()) { await db.AuctionStartDispatches.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted,true)); await db.Notifications.Where(x => x.Id == accepted.NotificationId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted,true)); }
        Assert.True((await Accept(r)).IsReplay); var e=await Assert.ThrowsAsync<DomainException>(() => Accept(r with { SourceEventId = Guid.NewGuid() })); Assert.Equal("AUCTION_START_ALREADY_NOTIFIED",e.ErrorCode); Assert.False((await Accept(Request() with { SellerUserId = r.SellerUserId })).IsReplay);
    }

    [PostgresFact]
    public async Task ConcurrentAliases_OnlyOneActivationNotice()
    {
        var r = Request(); var replies=await Task.WhenAll(new[] { r,r with { SourceEventId = Guid.NewGuid() } }.Select(async x => { try { await Accept(x); return 200; } catch(DomainException e) { Assert.Equal("AUCTION_START_ALREADY_NOTIFIED",e.ErrorCode); return e.StatusCode; } })); Assert.Single(replies,x => x==200); Assert.Single(replies,x => x==409);
    }

    [PostgresFact]
    public async Task OptionalAndMandatoryPairPolicy_DoesNotChangeOtherTypes()
    {
        var r = Request(); await Device(r.SellerUserId!.Value); await Preference(r.SellerUserId.Value,ChannelType.Push); await Preference(r.SellerUserId.Value,ChannelType.InApp);
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_STARTED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory,true));
        var accepted=await Accept(r); Assert.False(accepted.InboxVisible); Assert.Contains(accepted.Deliveries,d => d.Channel=="Push" && d.Status=="Pending");
        await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true)); Assert.Contains((await Accept(Request())).Deliveries,d => d.ErrorCode=="CHANNEL_DISABLED");
    }

    [PostgresFact]
    public async Task TemplateConfigurationRenderAndChannelErrors_AreAtomic()
    {
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_STARTED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,false)); await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.NotFound,"TEMPLATE_NOT_FOUND");
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_STARTED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,true).SetProperty(t => t.ChannelCode,ChannelType.Email)); await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.Conflict,"AUCTION_START_TEMPLATE_INVALID");
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_STARTED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.ChannelCode,ChannelType.Push).SetProperty(t => t.BodyTemplate,"{{WinnerEmail}}")); await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.BadRequest,"TEMPLATE_DATA_MISSING");
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_STARTED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{AuctionTitle")); await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.Conflict,"TEMPLATE_INVALID");
        await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted,true)); await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.Conflict,"CHANNEL_NOT_FOUND"); await using var verify=Db(); Assert.Empty(await verify.AuctionStartDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task GenericCollisionAndBrokenDeliveryManifest_DoNotAdoptUnknownState()
    {
        var r=Request(); await using(var db=Db()) await new DispatchService(db).CreateAsync(new() { SourceEventId=r.SourceEventId,UserId=r.SellerUserId,TemplateCode="AUCTION_STARTED_PUSH",Data=new() { ["AuctionTitle"]="Other",["StartedAtUtc"]="Other",["AuctionEndsAtUtc"]="Other" } });
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"AUCTION_START_CONFLICT"); var other=Request(); var accepted=await Accept(other);
        await using(var db=Db()) { await db.NotificationLogs.Where(l => l.NotificationId==accepted.NotificationId).ExecuteDeleteAsync(); await db.NotificationDeliveries.Where(d => d.NotificationId==accepted.NotificationId).ExecuteDeleteAsync(); }
        await Check(await _client.PostAsJsonAsync(Route,other),HttpStatusCode.Conflict,"DELIVERY_STATE_INVALID");
    }

    [PostgresFact]
    public async Task LaterSellerResult_DoesNotOverwriteHistoricalActivation()
    {
        var r=Request() with { ScheduledStartAt=_clock.GetUtcNow().AddDays(-2),StartedAt=_clock.GetUtcNow().AddDays(-2),AuctionEndsAt=_clock.GetUtcNow().AddDays(-1) }; var first=await Accept(r);
        await using(var db=Db()) await new Notification.Application.Services.AuctionSellerResults.AuctionSellerResultService(db,_clock).CreateAsync(new() { SourceEventId=Guid.NewGuid(),AuctionId=r.AuctionId,ResultId=Guid.NewGuid(),SellerUserId=r.SellerUserId,AuctionTitle=r.AuctionTitle,Outcome="NoBids",DecidedAt=r.AuctionEndsAt });
        Assert.Equal(2,(await Inbox(r.SellerUserId!.Value)).Items.Count); Assert.Equal(first.NotificationId,(await Accept(r)).NotificationId);
    }

    [PostgresFact]
    public async Task SwaggerAndPreferenceMatrix_NewTypeIsOptionalAndPreviousEnumsStable()
    {
        var swagger=await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route,swagger); Assert.Contains("{notificationId}",swagger); Assert.Contains("scheduledStartAt",swagger);
        Role("seller"); var prefs=(await _client.GetFromJsonAsync<List<NotificationPreferenceResponse>>("/api/v1/notifications/preferences"))!; Assert.Equal(54,prefs.Count); Assert.Equal(18,prefs.Count(p => p.IsMandatory)); Assert.All(prefs.Where(p => p.NotificationType=="AuctionStarted"),p => Assert.False(p.IsMandatory)); Assert.Equal(11,(int)NotificationType.AuctionStarted); Assert.Equal(10,(int)NotificationType.AuctionSellerResult); Assert.Equal(21,(int)AuditAction.AuctionStartAccepted);
    }

    [PostgresFact]
    public async Task ConcurrentSameEvent_AcceptsOnce_ChangedRecipientConflicts()
    {
        var r = Request(); var replies = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Accept(r))); Assert.Single(replies,x => !x.IsReplay); Assert.Single(replies.Select(x => x.NotificationId).Distinct());
        var other = Request(); var statuses = await Task.WhenAll(new[] { other,other with { SellerUserId = Guid.NewGuid() } }.Select(async x => { try { await Accept(x); return 200; } catch(DomainException ex) { Assert.Equal("AUCTION_START_CONFLICT",ex.ErrorCode); return ex.StatusCode; } })); Assert.Single(statuses,x => x == 200); Assert.Single(statuses,x => x == 409);
    }

    [PostgresFact]
    public async Task OptionalPreferences_SuppressEachChannelIndependently()
    {
        var a = Request(); await Device(a.SellerUserId!.Value); await Preference(a.SellerUserId.Value,ChannelType.Push); var ar = await Accept(a);
        Assert.Contains(ar.Deliveries,x => x.Channel == "Push" && x.ErrorCode == "PREFERENCE_DISABLED"); Assert.True(ar.InboxVisible); Assert.False(await Process());
        var b = Request(); await Device(b.SellerUserId!.Value); await Preference(b.SellerUserId.Value,ChannelType.InApp); var br = await Accept(b);
        Assert.False(br.InboxVisible); Assert.True(await Process()); Assert.Equal(b.SellerUserId,Assert.Single(_push.Messages).UserId); Assert.Empty((await Inbox(b.SellerUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task BothChannelsDisabled_HidesItemAndReplayNeverRequeues()
    {
        var r = Request(); await Device(r.SellerUserId!.Value);
        await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push || c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var result = await Accept(r); Assert.False(result.InboxVisible); Assert.All(result.Deliveries,d => Assert.Equal("Skipped",d.Status)); Assert.Empty((await Inbox(r.SellerUserId.Value)).Items);
        await using(var db = Db()) await db.NotificationChannels.ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,false));
        Assert.All((await Accept(r)).Deliveries,d => Assert.Equal("Skipped",d.Status)); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task OwnerDetailReadBadgeAndForeignProtection_UsesOneLogicalItem()
    {
        var r = Request(); await Device(r.SellerUserId!.Value); var accepted = await Accept(r); Role("seller",r.SellerUserId);
        var path = Route+"/"+accepted.NotificationId; await Check(await _client.GetAsync(path),HttpStatusCode.OK); Assert.Single((await Inbox(r.SellerUserId.Value,true)).Items);
        await Check(await _client.PutAsJsonAsync("/api/v1/notifications/"+accepted.NotificationId+"/read-states",new { isRead = true }),HttpStatusCode.OK);
        Assert.Empty((await Inbox(r.SellerUserId.Value,true)).Items); Role("buyer",Guid.NewGuid()); await Check(await _client.GetAsync(path),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
        Role(null); await Check(await _client.GetAsync(path),HttpStatusCode.Unauthorized,"UNAUTHORIZED");
        Role("seller",r.SellerUserId); await using(var db = Db()) await db.Notifications.Where(n => n.Id == accepted.NotificationId).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDeleted,true)); await Check(await _client.GetAsync(path),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
    }

    [PostgresFact]
    public async Task PushOnlyDetailIsOwned_AndNotInBell()
    {
        var r = Request(); await Device(r.SellerUserId!.Value); await Preference(r.SellerUserId.Value,ChannelType.InApp); var result = await Accept(r);
        Role("seller",r.SellerUserId); await Check(await _client.GetAsync(Route+"/"+result.NotificationId),HttpStatusCode.OK); Assert.Empty((await Inbox(r.SellerUserId.Value,true)).Items);
        Role("seller",Guid.NewGuid()); await Check(await _client.GetAsync(Route+"/"+result.NotificationId),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
    }

    [PostgresFact]
    public async Task NoDevicesOrOverflow_SkipOnlyPushAndKeepInbox()
    {
        var a = Request(); Assert.Contains((await Accept(a)).Deliveries,d => d.ErrorCode == "PUSH_NO_ACTIVE_DEVICES");
        var b = Request(); await Device(b.SellerUserId!.Value,101); Assert.Contains((await Accept(b)).Deliveries,d => d.ErrorCode == "PUSH_DEVICE_LIMIT");
        Assert.Single((await Inbox(a.SellerUserId!.Value)).Items); Assert.Single((await Inbox(b.SellerUserId.Value)).Items); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task RevokedDeviceAfterAcceptance_DoesNotSendToPreviousOwner()
    {
        var r = Request(); await Device(r.SellerUserId!.Value); var result = await Accept(r);
        await using(var db = Db()) await db.DeviceTokens.Where(d => d.UserId == r.SellerUserId).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive,false));
        Assert.True(await Process()); Assert.Empty(_push.Messages); Assert.Single((await Inbox(r.SellerUserId.Value)).Items); Assert.DoesNotContain((await Accept(r)).Deliveries,d => d.Channel == "Push" && d.Status == "Sent");
    }

    [PostgresFact]
    public async Task PersistenceFailure_RollsBackReceiptGroupAndAudit_ThenRetryAccepts()
    {
        var r = Request(); await using(var db = Db(new FailAllSave())) await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(r));
        await using(var db = Db()) { Assert.Empty(await db.AuctionStartDispatches.ToListAsync()); Assert.Empty(await db.Notifications.Where(n => n.SourceEventId == r.SourceEventId).ToListAsync()); Assert.Empty(await db.AuditLogs.Where(a => a.Action == AuditAction.AuctionStartAccepted).ToListAsync()); }
        Assert.False((await Accept(r)).IsReplay);
    }

    [PostgresFact]
    public async Task TemplateSharedLock_PreventsMetadataEditBeforeAcceptanceCommit()
    {
        var r = Request(); var gate = new GateSave(); await using var acceptingDb = Db(gate); var accepting = Service(acceptingDb).CreateAsync(r); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var editor = Db(); await using var tx = await editor.Database.BeginTransactionAsync(); await editor.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => editor.NotificationTemplates.Where(t => t.Code == "AUCTION_STARTED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.NotificationType,NotificationType.Promotion))); Assert.Equal("55P03",Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        finally { gate.Release.TrySetResult(); }
        Assert.False((await accepting.WaitAsync(TimeSpan.FromSeconds(5))).IsReplay);
    }

    [PostgresFact]
    public async Task RoleAndBodyLimit_OnlyServiceOrAdminCanAccept()
    {
        var r = Request(); Role(null); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Unauthorized,"UNAUTHORIZED");
        foreach(var role in new[] { "buyer","seller","staff" }) { Role(role); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Forbidden,"FORBIDDEN"); }
        Role("service"); await Check(await _client.PostAsJsonAsync(Route,r with { AuctionTitle = new string('x',17000) }),HttpStatusCode.RequestEntityTooLarge,"REQUEST_BODY_TOO_LARGE"); Role("admin"); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task WorkerFailureRetry_KeepsInboxAndOriginalPlainTextSnapshot()
    {
        var r = Request(); await Device(r.SellerUserId!.Value); _push.Result = _ => PushDeliveryResult.Failure("FCM_UNAVAILABLE","Test transient failure.",true); await Accept(r); Assert.True(await Process()); var original = Assert.Single(_push.Messages).Body;
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_STARTED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"Changed later"));
        _clock.Advance(TimeSpan.FromSeconds(10)); _push.Result = _ => PushDeliveryResult.Sent("ok"); Assert.True(await Process()); Assert.Equal(original,_push.Messages.Last().Body); Assert.Single((await Inbox(r.SellerUserId.Value)).Items); Assert.Equal("Completed",(await Accept(r)).NotificationStatus);
        var other = Request(); await Device(other.SellerUserId!.Value); await Accept(other); _push.Result = _ => PushDeliveryResult.Failure("FCM_AUTH_FAILED","Test permanent failure."); Assert.True(await Process()); Assert.Equal("Partial",(await Accept(other)).NotificationStatus); Assert.Single((await Inbox(other.SellerUserId.Value)).Items);
    }

    private sealed class GateSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct = default)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); return result; }
    }

    private async Task Preference(Guid owner,ChannelType channel,bool disabled = true)
    {
        await using var db = Db();
        db.NotificationPreferences.Add(new() { Id = Guid.NewGuid(),UserId = owner,NotificationType = NotificationType.AuctionStarted,ChannelCode = channel,IsDisabled = disabled });
        await db.SaveChangesAsync();
    }
    private async Task Device(Guid owner,int count = 1)
    {
        await using var db = Db();
        for(var i = 0;i < count;i++) db.DeviceTokens.Add(new() { Id = Guid.NewGuid(),UserId = owner,Token = "test-" + Guid.NewGuid().ToString("N"),IsActive = true,Platform = DevicePlatform.Web });
        await db.SaveChangesAsync();
    }
    private async Task<PagedData<InAppNotificationItemResponse>> Inbox(Guid user,bool unread = false)
    {
        Role("seller",user);
        return (await _client.GetFromJsonAsync<PagedData<InAppNotificationItemResponse>>("/api/v1/notifications/me" + (unread ? "?isRead=false" : "")))!;
    }
    private sealed class FakePush : IPushDeliverySender
    {
        public List<PushDeliveryMessage> Messages { get; } = new();
        public Func<PushDeliveryMessage,PushDeliveryResult> Result { get; set; } = _ => PushDeliveryResult.Sent("test-provider-id");
        public Task<PushDeliveryResult> SendAsync(PushDeliveryMessage message,CancellationToken ct = default) { Messages.Add(message); return Task.FromResult(Result(message)); }
    }
    private sealed class FailAllSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct = default)
            => throw new InvalidOperationException("Test acceptance audit failure.");
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
}
