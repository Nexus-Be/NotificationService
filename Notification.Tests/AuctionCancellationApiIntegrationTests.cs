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
using Notification.Application.Services.AuctionCancellations;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class AuctionCancellationApiIntegrationTests : IAsyncLifetime
{
    private const string Route = "/api/v1/notifications/auction-cancellations";
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
    private AuctionCancellationService Service(AppDbContext db) => new(db,_clock);
    private CreateAuctionCancellationRequest Request() => new()
    {
        SourceEventId = Guid.NewGuid(),AuctionId = Guid.NewGuid(),SellerUserId = Guid.NewGuid(),RecipientUserId = Guid.NewGuid(),
        CancellationKind = "Admin",RecipientKind = "Participant",BidCount = 5,AuctionTitle = "Example auction",Reason = "Policy review",CancelledAt = _clock.GetUtcNow()
    };
    private async Task<AuctionCancellationResponse> Accept(CreateAuctionCancellationRequest request)
    { await using var db = Db(); return await Service(db).CreateAsync(request); }
    private async Task<bool> Process()
    { await using var db = Db(); return await new DeliveryService(db,_sender,new(),_clock,_push).ProcessNextAsync(); }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_auction_cancel_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(AuctionCancellationsController).Assembly)
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
    public async Task MultipleRecipientsShareOneHeader_ButEachHasOnePrivateLogicalItem()
    {
        var r = Request(); var second = Guid.NewGuid();
        await Device(r.RecipientUserId!.Value,2); await Device(second); await Device(r.SellerUserId!.Value);
        var a = await Accept(r); var b = await Accept(r with { RecipientUserId = second }); var seller = await Accept(r with { RecipientUserId = r.SellerUserId,RecipientKind = "Seller" });
        Assert.False(a.IsReplay); Assert.False(b.IsReplay); Assert.False(seller.IsReplay); Assert.NotEqual(a.NotificationId,b.NotificationId);
        Assert.Equal(3,a.Deliveries.Count); Assert.Equal(2,b.Deliveries.Count);
        var item = Assert.Single((await Inbox(r.RecipientUserId.Value)).Items); Assert.Single((await Inbox(second)).Items); Assert.Single((await Inbox(r.SellerUserId.Value)).Items);
        Assert.DoesNotContain(second.ToString(),item.Payload); Assert.DoesNotContain(r.SellerUserId.ToString()!,item.Payload); Assert.Contains("hủy",item.Body);
        Assert.DoesNotContain("hoàn tiền",item.Body); Assert.DoesNotContain("phạt tiền",item.Body);
        for(var i = 0;i < 4;i++) Assert.True(await Process()); Assert.False(await Process());
        Assert.Equal(4,_push.Messages.Count); Assert.Single((await Inbox(r.RecipientUserId.Value)).Items);
        await using var db = Db(); Assert.Single(await db.AuctionCancellationDispatches.ToListAsync()); Assert.Equal(3,await db.AuctionCancellationRecipients.CountAsync());
        Assert.Equal(3,await db.AuditLogs.CountAsync(a => a.Action == AuditAction.AuctionCancellationAccepted)); Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task SellerNoBidCancellation_AllowsSellerAcknowledgementOnly()
    {
        var r = Request(); r = r with { CancellationKind = "Seller",BidCount = 0,RecipientKind = "Seller",RecipientUserId = r.SellerUserId };
        var response = await _client.PostAsJsonAsync(Route,r); await Check(response,HttpStatusCode.OK); Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Single((await Inbox(r.SellerUserId!.Value)).Items);
    }

    [PostgresFact]
    public async Task Http_RejectsUntrustedSendersAndInvalidDeclaredCancellationRecipients()
    {
        var r = Request(); Role(null); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Unauthorized);
        foreach(var role in new[] { "buyer","seller","staff" }) { Role(role); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Forbidden); }
        Role("admin");
        foreach(var invalid in new[] { r with { SourceEventId = null },r with { AuctionId = Guid.Empty },r with { SellerUserId = Guid.Empty },r with { RecipientUserId = null },
            r with { BidCount = null },r with { BidCount = -1 },r with { BidCount = 0 },r with { RecipientUserId = r.SellerUserId },r with { RecipientKind = "Seller" },
            r with { CancellationKind = "Seller" },r with { CancellationKind = "Unknown" },r with { RecipientKind = "All" },r with { Reason = " " },r with { Reason = new string('a',501) },
            r with { AuctionTitle = "bad\nname" },r with { CancelledAt = null },r with { CancelledAt = _clock.GetUtcNow().AddMinutes(1) } })
            await Check(await _client.PostAsJsonAsync(Route,invalid),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        var body = JsonSerializer.SerializeToNode(r)!.AsObject(); body["recipients"] = JsonSerializer.SerializeToNode(new[] { Guid.NewGuid(),Guid.NewGuid() });
        await Check(await _client.PostAsync(Route,new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json")),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await Check(await _client.PostAsync(Route,new StringContent("{\"reason\":\"" + new string('a',17000) + "\"}",Encoding.UTF8,"application/json")),HttpStatusCode.RequestEntityTooLarge,"REQUEST_BODY_TOO_LARGE");
    }

    [PostgresFact]
    public async Task HeaderCannotChangeForAnotherRecipient_AfterFirstAcceptance()
    {
        var r = Request(); await Accept(r);
        foreach(var conflict in new[] { r with { RecipientUserId = Guid.NewGuid(),Reason = "Changed" },r with { AuctionTitle = "Changed" },r with { AuctionId = Guid.NewGuid() },
            r with { SellerUserId = Guid.NewGuid() },r with { BidCount = 6 },r with { CancelledAt = r.CancelledAt!.Value.AddSeconds(-1) } })
            await Check(await _client.PostAsJsonAsync(Route,conflict),HttpStatusCode.Conflict,"AUCTION_CANCELLATION_CONFLICT");
        await using var db = Db(); Assert.Single(await db.AuctionCancellationRecipients.ToListAsync());
    }

    [PostgresFact]
    public async Task RecipientReplay_NormalizesHeaderAndIgnoresTraceTemplateAndNewDevices()
    {
        var r = Request(); var first = await Accept(r); await Device(r.RecipientUserId!.Value);
        await using(var db = Db())
        {
            await db.NotificationTemplates.Where(t => t.Code == "AUCTION_CANCELLED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,false));
            await db.AuctionCancellationDispatches.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted,true)); await db.AuctionCancellationRecipients.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted,true));
        }
        var replay = await Accept(r with { AuctionTitle = " " + r.AuctionTitle + " ",Reason = " " + r.Reason + " ",CancellationKind = "admin",RecipientKind = "participant",
            CancelledAt = r.CancelledAt!.Value.ToOffset(TimeSpan.FromHours(7)),TraceId = "another-delivery-trace" });
        Assert.True(replay.IsReplay); Assert.Equal(first.NotificationId,replay.NotificationId); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task AnotherEventForSameAuction_IsRejectedEvenForNewRecipient()
    {
        var r = Request(); await Accept(r);
        await Check(await _client.PostAsJsonAsync(Route,r with { SourceEventId = Guid.NewGuid(),RecipientUserId = Guid.NewGuid() }),HttpStatusCode.Conflict,"AUCTION_CANCELLATION_ALREADY_NOTIFIED");
    }

    [PostgresFact]
    public async Task ConcurrentSameRecipient_AcceptsOnceAndReplaysSevenTimes()
    {
        var r = Request(); var results = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Accept(r))).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results,x => !x.IsReplay); Assert.Single(results.Select(x => x.NotificationId).Distinct());
        await using var db = Db(); Assert.Single(await db.AuctionCancellationDispatches.ToListAsync()); Assert.Single(await db.AuctionCancellationRecipients.ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentDifferentRecipientsShareHeader_AndAllAcceptIndependently()
    {
        var r = Request(); var results = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Accept(r with { RecipientUserId = Guid.NewGuid() }))).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.All(results,x => Assert.False(x.IsReplay)); Assert.Equal(8,results.Select(x => x.NotificationId).Distinct().Count());
        await using var db = Db(); Assert.Single(await db.AuctionCancellationDispatches.ToListAsync()); Assert.Equal(8,await db.AuctionCancellationRecipients.CountAsync()); Assert.Equal(8,await db.Notifications.CountAsync(n => n.SourceEventId == r.SourceEventId));
    }

    [PostgresFact]
    public async Task ConcurrentSameEventWithDifferentHeaders_OneWinsOtherConflicts()
    {
        var r = Request(); var statuses = await Task.WhenAll(new[] { r,r with { RecipientUserId = Guid.NewGuid(),Reason = "Different" } }.Select(async x =>
        { try { await Accept(x); return 200; } catch(DomainException ex) { Assert.Equal("AUCTION_CANCELLATION_CONFLICT",ex.ErrorCode); return ex.StatusCode; } }));
        Assert.Single(statuses,x => x == 200); Assert.Single(statuses,x => x == 409);
    }

    [PostgresFact]
    public async Task ConcurrentEventAliasesForOneAuction_OnlyOneHeaderCanAccept()
    {
        var r = Request(); var statuses = await Task.WhenAll(new[] { r,r with { SourceEventId = Guid.NewGuid(),RecipientUserId = Guid.NewGuid() } }.Select(async x =>
        { try { await Accept(x); return 200; } catch(DomainException ex) { Assert.Equal("AUCTION_CANCELLATION_ALREADY_NOTIFIED",ex.ErrorCode); return ex.StatusCode; } }));
        Assert.Single(statuses,x => x == 200); Assert.Single(statuses,x => x == 409);
    }

    [PostgresFact]
    public async Task LaterRecipientAuditFailure_DoesNotLoseEarlierRecipientOrHeader()
    {
        var r = Request(); var first = await Accept(r); var next = r with { RecipientUserId = Guid.NewGuid() };
        await using(var db = Db(new FailAllSave())) await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(next));
        await using var verify = Db(); Assert.Single(await verify.AuctionCancellationDispatches.ToListAsync()); Assert.Single(await verify.AuctionCancellationRecipients.ToListAsync());
        Assert.Equal(first.NotificationId,(await Accept(r)).NotificationId); Assert.False((await Accept(next)).IsReplay);
    }

    [PostgresFact]
    public async Task FirstRecipientAuditFailure_RollsBackNewHeaderAndEveryDelivery()
    {
        var r = Request(); await using(var db = Db(new FailAllSave())) await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(r));
        await using var verify = Db(); Assert.Empty(await verify.AuctionCancellationDispatches.ToListAsync()); Assert.Empty(await verify.AuctionCancellationRecipients.ToListAsync());
        Assert.False(await verify.Notifications.AnyAsync(n => n.SourceEventId == r.SourceEventId)); Assert.False((await Accept(r)).IsReplay);
    }

    [PostgresFact]
    public async Task MandatoryPushIgnoresLegacyOptOut_InAppPreferenceStillApplies()
    {
        var r = Request(); await Device(r.RecipientUserId!.Value); await Preference(r.RecipientUserId.Value,ChannelType.Push); await Preference(r.RecipientUserId.Value,ChannelType.InApp);
        var result = await Accept(r); Assert.False(result.InboxVisible); Assert.Contains(result.Deliveries,d => d.Channel == "Push" && d.Status == "Pending");
        Assert.Empty((await Inbox(r.RecipientUserId.Value)).Items); await Check(await _client.GetAsync(Route + "/" + result.NotificationId),HttpStatusCode.OK);
        Assert.True(await Process()); Assert.Single(_push.Messages);
    }

    [PostgresFact]
    public async Task GlobalPushDisableStillWinsMandatory_AndNoDeviceStillKeepsAllowedInbox()
    {
        var r = Request(); await Device(r.RecipientUserId!.Value);
        await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var result = await Accept(r); Assert.Contains(result.Deliveries,d => d.Channel == "Push" && d.ErrorCode == "CHANNEL_DISABLED"); Assert.Single((await Inbox(r.RecipientUserId.Value)).Items); Assert.False(await Process());
        await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,false));
        Assert.True((await Accept(r)).IsReplay); Assert.False(await Process());
        var noDevice = r with { RecipientUserId = Guid.NewGuid() }; Assert.Contains((await Accept(noDevice)).Deliveries,d => d.ErrorCode == "PUSH_NO_ACTIVE_DEVICES");
    }

    [PostgresFact]
    public async Task GlobalInAppDisable_DoesNotPreventPushOrExposeBellItem()
    {
        var r = Request(); await Device(r.RecipientUserId!.Value); await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var result = await Accept(r); Assert.False(result.InboxVisible); Assert.True(await Process()); Assert.Empty((await Inbox(r.RecipientUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task BothChannelsDisabled_HideItemAndDoNotCallProvider()
    {
        var r = Request(); await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push || c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var result = await Accept(r); Assert.False(result.InboxVisible); Assert.All(result.Deliveries,d => Assert.Equal("Skipped",d.Status)); Assert.False(await Process());
        Assert.Empty((await Inbox(r.RecipientUserId!.Value)).Items); await Check(await _client.GetAsync(Route + "/" + result.NotificationId),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
    }

    [PostgresFact]
    public async Task Over100Devices_SkipsOnlyThisRecipientsPush_AndKeepsInbox()
    {
        var r = Request(); await Device(r.RecipientUserId!.Value,101); var result = await Accept(r);
        Assert.Contains(result.Deliveries,d => d.ErrorCode == "PUSH_DEVICE_LIMIT"); Assert.Single((await Inbox(r.RecipientUserId.Value)).Items); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task OneRecipientPushFailure_DoesNotLoseTheirInboxOrOtherRecipientPush()
    {
        var r = Request(); var other = Guid.NewGuid(); await Device(r.RecipientUserId!.Value); await Device(other);
        _push.Result = m => m.UserId == r.RecipientUserId ? PushDeliveryResult.Failure("FCM_AUTH_FAILED","Test permanent failure.") : PushDeliveryResult.Sent("other-ok");
        await Accept(r); await Accept(r with { RecipientUserId = other }); await Process(); await Process();
        Assert.Equal("Partial",(await Accept(r)).NotificationStatus); Assert.Equal("Completed",(await Accept(r with { RecipientUserId = other })).NotificationStatus);
        Assert.Single((await Inbox(r.RecipientUserId.Value)).Items); Assert.Single((await Inbox(other)).Items);
    }

    [PostgresFact]
    public async Task TransientRetry_OnlyResendsFailedDeviceWithoutNewLogicalItem()
    {
        var r = Request(); await Device(r.RecipientUserId!.Value,2); var calls = 0;
        _push.Result = _ => calls++ == 0 ? PushDeliveryResult.Failure("FCM_UNAVAILABLE","Test retry.",true) : PushDeliveryResult.Sent("ok");
        await Accept(r); await Process(); await Process(); Assert.False(await Process()); _clock.Advance(TimeSpan.FromSeconds(10)); Assert.True(await Process());
        Assert.Equal(3,_push.Messages.Count); Assert.Single((await Inbox(r.RecipientUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task OwnerDetailAndReadState_DoNotExposeOtherRecipientsOrMutateDelivery()
    {
        var r = Request(); var a = await Accept(r); var other = Guid.NewGuid(); var b = await Accept(r with { RecipientUserId = other });
        Role(null); await Check(await _client.GetAsync(Route + "/" + a.NotificationId),HttpStatusCode.Unauthorized);
        Role("buyer",r.RecipientUserId); await Check(await _client.GetAsync(Route + "/" + b.NotificationId),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
        var detail = await _client.GetAsync(Route + "/" + a.NotificationId); await Check(detail,HttpStatusCode.OK); Assert.True(detail.Headers.CacheControl!.NoStore);
        await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + a.NotificationId + "/read-states",new { isRead = true }),HttpStatusCode.OK);
        Assert.Empty((await Inbox(r.RecipientUserId!.Value,true)).Items); Assert.Single((await Inbox(other,true)).Items);
    }

    [PostgresFact]
    public async Task InvalidTemplateMetadataAndMissingChannel_DoNotCreateHeader()
    {
        var r = Request(); await using var db = Db();
        await db.NotificationTemplates.Where(t => t.Code == "AUCTION_CANCELLED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory,false));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"AUCTION_CANCELLATION_TEMPLATE_INVALID");
        await db.NotificationTemplates.Where(t => t.Code == "AUCTION_CANCELLED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory,true).SetProperty(t => t.IsActive,false));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.NotFound,"TEMPLATE_NOT_FOUND");
        await db.NotificationTemplates.Where(t => t.Code == "AUCTION_CANCELLED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,true));
        await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted,true));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"CHANNEL_NOT_FOUND"); Assert.Empty(await db.AuctionCancellationDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task MissingTemplateDataOrInvalidPlaceholder_RollsBackRecipientAndNewHeader()
    {
        var r = Request(); await using var db = Db();
        await db.NotificationTemplates.Where(t => t.Code == "AUCTION_CANCELLED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{PrivateEvidence}}"));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.BadRequest,"TEMPLATE_DATA_MISSING");
        await db.NotificationTemplates.Where(t => t.Code == "AUCTION_CANCELLED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{invalid"));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"TEMPLATE_INVALID"); Assert.Empty(await db.AuctionCancellationRecipients.ToListAsync()); Assert.Empty(await db.AuctionCancellationDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task GenericDispatchCollision_IsNotAdoptedAsVerifiedCancellation()
    {
        var r = Request(); await using(var db = Db()) await new DispatchService(db).CreateAsync(new()
        {
            SourceEventId = r.SourceEventId,UserId = r.RecipientUserId,TemplateCode = "AUCTION_CANCELLED_PUSH",
            Data = new() { ["AuctionTitle"] = "Other",["Reason"] = "Other",["CancelledAtUtc"] = "Other" }
        });
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"AUCTION_CANCELLATION_CONFLICT");
        await using var verify = Db(); Assert.Empty(await verify.AuctionCancellationDispatches.ToListAsync()); Assert.Empty(await verify.AuctionCancellationRecipients.ToListAsync());
    }

    [PostgresFact]
    public async Task SwaggerAndNewTypePreferences_AreCompatibleAndTyped()
    {
        var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route,swagger); Assert.Contains("{notificationId}",swagger); Assert.Contains("recipientKind",swagger);
        var r = Request(); Role("buyer",r.RecipientUserId); var prefs = (await _client.GetFromJsonAsync<List<NotificationPreferenceResponse>>("/api/v1/notifications/preferences"))!;
        Assert.Equal(54,prefs.Count); Assert.True(prefs.Single(p => p.NotificationType == "AuctionCancelled" && p.ChannelCode == "Push").IsMandatory);
        Assert.False(prefs.Single(p => p.NotificationType == "AuctionCancelled" && p.ChannelCode == "InApp").IsMandatory);
    }

    [PostgresFact]
    public async Task HistoricalCancellation_PreservesEffectiveTimeAndAvoidsRefundClaims()
    {
        var r = Request() with { CancelledAt = _clock.GetUtcNow().AddDays(-2) }; await Accept(r);
        var item = Assert.Single((await Inbox(r.RecipientUserId!.Value)).Items); using var payload = JsonDocument.Parse(item.Payload!);
        Assert.Equal(new DateTimeOffset(r.CancelledAt!.Value.UtcTicks / 10 * 10,TimeSpan.Zero),payload.RootElement.GetProperty("cancelledAt").GetDateTimeOffset());
        Assert.DoesNotContain("hoàn tiền",item.Body); Assert.DoesNotContain("nhả kho",item.Body);
    }

    [PostgresFact]
    public async Task TemplateSharedLock_KeepsCancellationPolicyStableUntilRecipientCommit()
    {
        var r = Request(); var gate = new GateSave(); await using var acceptingDb = Db(gate);
        var accepting = Service(acceptingDb).CreateAsync(r); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var editor = Db(); await using var tx = await editor.Database.BeginTransactionAsync(); await editor.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => editor.NotificationTemplates.Where(t => t.Code == "AUCTION_CANCELLED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory,false)));
            Assert.Equal("55P03",Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        finally { gate.Release.TrySetResult(); }
        Assert.False((await accepting.WaitAsync(TimeSpan.FromSeconds(5))).IsReplay);
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
        db.NotificationPreferences.Add(new() { Id = Guid.NewGuid(),UserId = owner,NotificationType = NotificationType.AuctionCancelled,ChannelCode = channel,IsDisabled = disabled });
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
        Role("buyer",user);
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
