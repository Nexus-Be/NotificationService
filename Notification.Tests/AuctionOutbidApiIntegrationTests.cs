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
using Notification.Application.Services.AuctionOutbids;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class AuctionOutbidApiIntegrationTests : IAsyncLifetime
{
    private const string Route = "/api/v1/notifications/auction-outbids";
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
    private AuctionOutbidService Service(AppDbContext db) => new(db,_clock);
    private CreateAuctionOutbidRequest Request() => new()
    {
        SourceEventId = Guid.NewGuid(),AuctionId = Guid.NewGuid(),TriggerBidId = Guid.NewGuid(),PreviousBidId = Guid.NewGuid(),
        PreviousHighestBidderUserId = Guid.NewGuid(),NewHighestBidderUserId = Guid.NewGuid(),SellerUserId = Guid.NewGuid(),
        AuctionTitle = "Example auction",PreviousAmount = 100000m,NewAmount = 110000m,OccurredAt = _clock.GetUtcNow(),AuctionEndsAt = _clock.GetUtcNow().AddHours(1)
    };
    private async Task<AuctionOutbidResponse> Accept(CreateAuctionOutbidRequest request)
    { await using var db = Db(); return await Service(db).CreateAsync(request); }
    private async Task<bool> Process()
    { await using var db = Db(); return await new DeliveryService(db,_sender,new(),_clock,_push).ProcessNextAsync(); }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_auction_outbid_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(AuctionOutbidsController).Assembly)
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
    public async Task Http_TargetsOnlyPreviousHighestBidder_WithGroupedChannelsAndPrivatePayload()
    {
        var r = Request(); await Device(r.PreviousHighestBidderUserId!.Value,3); await Device(r.NewHighestBidderUserId!.Value); await Device(r.SellerUserId!.Value);
        var response = await _client.PostAsJsonAsync(Route,r); await Check(response,HttpStatusCode.OK); Assert.True(response.Headers.CacheControl!.NoStore);
        var accepted = (await response.Content.ReadFromJsonAsync<AuctionOutbidResponse>())!;
        Assert.Equal(r.PreviousHighestBidderUserId,accepted.UserId); Assert.Equal(4,accepted.Deliveries.Count); Assert.Empty(_push.Messages);
        var item = Assert.Single((await Inbox(r.PreviousHighestBidderUserId.Value)).Items); Assert.Contains("vượt giá",item.Body); Assert.Contains("110,000.00",item.Body); Assert.Contains("UTC",item.Body);
        Assert.Empty((await Inbox(r.NewHighestBidderUserId.Value)).Items); Assert.Empty((await Inbox(r.SellerUserId.Value)).Items);
        Assert.DoesNotContain(r.NewHighestBidderUserId.ToString()!,item.Payload); Assert.DoesNotContain(r.SellerUserId.ToString()!,item.Payload);
        for(var i = 0;i < 3;i++) Assert.True(await Process()); Assert.False(await Process());
        Assert.All(_push.Messages,m => { Assert.Equal(r.PreviousHighestBidderUserId,m.UserId); Assert.Equal(accepted.NotificationId,m.NotificationId); });
        Assert.Single((await Inbox(r.PreviousHighestBidderUserId.Value)).Items);
        await using var db = Db(); Assert.Single(await db.AuctionOutbidDispatches.ToListAsync()); Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditAction.AuctionOutbidAccepted).ToListAsync()); Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task NoDevices_PreservesUnreadInboxAndSkippedPush()
    {
        var r = Request(); var result = await Accept(r); Assert.True(result.InboxVisible); Assert.Equal("Completed",result.NotificationStatus);
        Assert.Contains(result.Deliveries,d => d.ErrorCode == "PUSH_NO_ACTIVE_DEVICES"); Assert.Single((await Inbox(r.PreviousHighestBidderUserId!.Value,true)).Items); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task OptionalPushPreference_CanSuppressPushWithoutLosingInApp()
    {
        var r = Request(); await Device(r.PreviousHighestBidderUserId!.Value); await Preference(r.PreviousHighestBidderUserId.Value,ChannelType.Push);
        var result = await Accept(r); Assert.Contains(result.Deliveries,d => d.Channel == "Push" && d.ErrorCode == "PREFERENCE_DISABLED");
        Assert.Single((await Inbox(r.PreviousHighestBidderUserId.Value)).Items); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task OptionalInAppPreference_AllowsPushOnlyAndOwnedClickDetail()
    {
        var r = Request(); await Device(r.PreviousHighestBidderUserId!.Value); await Preference(r.PreviousHighestBidderUserId.Value,ChannelType.InApp);
        var result = await Accept(r); Assert.False(result.InboxVisible); Assert.Empty((await Inbox(r.PreviousHighestBidderUserId.Value)).Items);
        await Check(await _client.GetAsync(Route + "/" + result.NotificationId),HttpStatusCode.OK); Assert.True(await Process()); Assert.Single(_push.Messages);
        await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + result.NotificationId + "/read-states",new { isRead = false }),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
    }

    [PostgresFact]
    public async Task BothOptionalPreferences_SuppressBothChannelsAndKeepReplayStable()
    {
        var r = Request(); await Device(r.PreviousHighestBidderUserId!.Value); await Preference(r.PreviousHighestBidderUserId.Value,ChannelType.InApp); await Preference(r.PreviousHighestBidderUserId.Value,ChannelType.Push);
        var result = await Accept(r); Assert.False(result.InboxVisible); Assert.All(result.Deliveries,d => Assert.Equal("Skipped",d.Status));
        await using(var db = Db()) await db.NotificationPreferences.Where(p => p.UserId == r.PreviousHighestBidderUserId).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDisabled,false));
        Assert.True((await Accept(r)).IsReplay); Assert.False(await Process()); Assert.Empty((await Inbox(r.PreviousHighestBidderUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task PushMandatoryOverride_DoesNotForceInAppAndGlobalDisableStillWins()
    {
        var r = Request(); await Device(r.PreviousHighestBidderUserId!.Value); await Preference(r.PreviousHighestBidderUserId.Value,ChannelType.InApp); await Preference(r.PreviousHighestBidderUserId.Value,ChannelType.Push);
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_BID_OUTBID_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory,true));
        var result = await Accept(r); Assert.False(result.InboxVisible); Assert.Contains(result.Deliveries,d => d.Channel == "Push" && d.Status == "Pending"); Assert.True(await Process());
        await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var next = await Accept(Request() with { PreviousHighestBidderUserId = r.PreviousHighestBidderUserId });
        Assert.Contains(next.Deliveries,d => d.Channel == "Push" && d.ErrorCode == "CHANNEL_DISABLED"); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task InAppMandatoryPair_OverridesOnlyItsDisabledPreference()
    {
        var r = Request(); await Preference(r.PreviousHighestBidderUserId!.Value,ChannelType.InApp); await Preference(r.PreviousHighestBidderUserId.Value,ChannelType.Push);
        await using(var db = Db())
        {
            db.NotificationTemplates.Add(new() { Id = Guid.NewGuid(),Code = "OUTBID_MANDATORY_INAPP_TEST",Locale = "vi-VN",NotificationType = NotificationType.AuctionOutbid,ChannelCode = ChannelType.InApp,
                SubjectTemplate = "Outbid",BodyTemplate = "Outbid",IsActive = true,IsMandatory = true }); await db.SaveChangesAsync();
        }
        var result = await Accept(r); Assert.True(result.InboxVisible); Assert.Contains(result.Deliveries,d => d.Channel == "Push" && d.ErrorCode == "PREFERENCE_DISABLED");
        Assert.Single((await Inbox(r.PreviousHighestBidderUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task GlobalInAppDisable_DoesNotPreventPushAndDoesNotCreateBellItem()
    {
        var r = Request(); await Device(r.PreviousHighestBidderUserId!.Value);
        await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var result = await Accept(r); Assert.False(result.InboxVisible); Assert.True(await Process()); Assert.Single(_push.Messages); Assert.Empty((await Inbox(r.PreviousHighestBidderUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task DeviceOverflow_SkipsPushOnlyAndKeepsAllowedInApp()
    {
        var r = Request(); await Device(r.PreviousHighestBidderUserId!.Value,101); var result = await Accept(r);
        Assert.Contains(result.Deliveries,d => d.ErrorCode == "PUSH_DEVICE_LIMIT"); Assert.Single((await Inbox(r.PreviousHighestBidderUserId.Value)).Items); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task PermanentPushFailure_PreservesLogicalInboxAndReportsPartial()
    {
        var r = Request(); await Device(r.PreviousHighestBidderUserId!.Value); _push.Result = _ => PushDeliveryResult.Failure("FCM_AUTH_FAILED","Test permanent failure.");
        await Accept(r); Assert.True(await Process()); Assert.Equal("Partial",(await Accept(r)).NotificationStatus); Assert.Single((await Inbox(r.PreviousHighestBidderUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task Retry_OnlyResendsFailedDeviceWithoutDuplicatingInbox()
    {
        var r = Request(); await Device(r.PreviousHighestBidderUserId!.Value,2); var calls = 0;
        _push.Result = _ => calls++ == 0 ? PushDeliveryResult.Failure("FCM_UNAVAILABLE","Test transient.",true) : PushDeliveryResult.Sent("ok");
        await Accept(r); await Process(); await Process(); Assert.False(await Process()); _clock.Advance(TimeSpan.FromSeconds(10)); await Process();
        Assert.Equal(3,_push.Messages.Count); Assert.Single((await Inbox(r.PreviousHighestBidderUserId.Value)).Items); Assert.Equal("Completed",(await Accept(r)).NotificationStatus);
    }

    [PostgresFact]
    public async Task DeviceReassignedAfterAcceptance_IsNotSentToNewOwner()
    {
        var r = Request(); await Device(r.PreviousHighestBidderUserId!.Value); await Accept(r);
        await using(var db = Db()) await db.DeviceTokens.Where(d => d.UserId == r.PreviousHighestBidderUserId).ExecuteUpdateAsync(s => s.SetProperty(d => d.UserId,r.NewHighestBidderUserId!.Value));
        Assert.True(await Process()); Assert.Empty(_push.Messages); Assert.Single((await Inbox(r.PreviousHighestBidderUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task Http_EnforcesSourceRolesAndRejectsInvalidBidRecipientAndPriceFields()
    {
        var r = Request(); Role(null); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Unauthorized);
        foreach(var role in new[] { "buyer","seller","staff" }) { Role(role); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Forbidden); }
        Role("admin");
        foreach(var invalid in new[] { r with { SourceEventId = null },r with { AuctionId = Guid.Empty },r with { TriggerBidId = r.PreviousBidId },r with { PreviousBidId = Guid.Empty },
            r with { PreviousHighestBidderUserId = r.NewHighestBidderUserId },r with { PreviousHighestBidderUserId = r.SellerUserId },r with { NewHighestBidderUserId = r.SellerUserId },
            r with { AuctionTitle = "bad\nname" },r with { PreviousAmount = 0 },r with { NewAmount = r.PreviousAmount },r with { NewAmount = 110000.001m },
            r with { Currency = "USD" },r with { OccurredAt = null },r with { OccurredAt = _clock.GetUtcNow().AddMinutes(1) },r with { AuctionEndsAt = r.OccurredAt },r with { TraceId = new string('a',101) } })
            await Check(await _client.PostAsJsonAsync(Route,invalid),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        var body = JsonSerializer.SerializeToNode(r)!.AsObject(); body["token"] = "do-not-accept-token";
        var response = await _client.PostAsync(Route,new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json")); await Check(response,HttpStatusCode.BadRequest,"VAL_INVALID_INPUT"); Assert.DoesNotContain("do-not-accept-token",await response.Content.ReadAsStringAsync());
        await Check(await _client.PostAsync(Route,new StringContent("{\"auctionTitle\":\"" + new string('a',17000) + "\"}",Encoding.UTF8,"application/json")),HttpStatusCode.RequestEntityTooLarge,"REQUEST_BODY_TOO_LARGE");
    }

    [PostgresFact]
    public async Task OwnerDetailAndReadStates_RejectOpponentSellerAndKeepBadgeCorrect()
    {
        var r = Request(); var result = await Accept(r); Role(null); await Check(await _client.GetAsync(Route + "/" + result.NotificationId),HttpStatusCode.Unauthorized);
        foreach(var user in new[] { r.NewHighestBidderUserId,r.SellerUserId }) { Role("buyer",user); await Check(await _client.GetAsync(Route + "/" + result.NotificationId),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND"); }
        Role("buyer",r.PreviousHighestBidderUserId); var detail = await _client.GetAsync(Route + "/" + result.NotificationId); await Check(detail,HttpStatusCode.OK); Assert.True(detail.Headers.CacheControl!.NoStore);
        await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + result.NotificationId + "/read-states",new { isRead = true }),HttpStatusCode.OK);
        Assert.Empty((await Inbox(r.PreviousHighestBidderUserId!.Value,true)).Items);
        await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + result.NotificationId + "/read-states",new { isRead = false }),HttpStatusCode.OK);
        Assert.Single((await Inbox(r.PreviousHighestBidderUserId.Value,true)).Items);
    }

    [PostgresFact]
    public async Task Replay_NormalizesAmountsTimeAndTraceWithoutTemplateOrNewDeviceFanout()
    {
        var r = Request(); var first = await Accept(r); await Device(r.PreviousHighestBidderUserId!.Value);
        await using(var db = Db()) { await db.NotificationTemplates.Where(t => t.Code == "AUCTION_BID_OUTBID_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,false)); await db.AuctionOutbidDispatches.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted,true)); }
        var replay = await Accept(r with { PreviousAmount = 100000.00m,NewAmount = 110000.000m,Currency = "vnd",AuctionTitle = " " + r.AuctionTitle + " ",OccurredAt = r.OccurredAt!.Value.ToOffset(TimeSpan.FromHours(7)),TraceId = "retry" });
        Assert.True(replay.IsReplay); Assert.Equal(first.NotificationId,replay.NotificationId); Assert.False(await Process());
        await using var verify = Db(); Assert.Single(await verify.AuditLogs.Where(a => a.Action == AuditAction.AuctionOutbidAccepted).ToListAsync());
    }

    [PostgresFact]
    public async Task SameEventDifferentBusinessFields_Conflicts()
    {
        var r = Request(); await Accept(r);
        foreach(var conflict in new[] { r with { AuctionId = Guid.NewGuid() },r with { TriggerBidId = Guid.NewGuid() },r with { PreviousBidId = Guid.NewGuid() },r with { PreviousHighestBidderUserId = Guid.NewGuid() },
            r with { NewHighestBidderUserId = Guid.NewGuid() },r with { NewAmount = 120000 },r with { AuctionEndsAt = r.AuctionEndsAt!.Value.AddMinutes(1) } })
            await Check(await _client.PostAsJsonAsync(Route,conflict),HttpStatusCode.Conflict,"AUCTION_OUTBID_CONFLICT");
    }

    [PostgresFact]
    public async Task ConcurrentSameEvent_AcceptsOnceAndReplaysSevenTimes()
    {
        var r = Request(); var results = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Accept(r))).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results,x => !x.IsReplay); Assert.Single(results.Select(x => x.NotificationId).Distinct());
        await using var db = Db(); Assert.Single(await db.AuctionOutbidDispatches.ToListAsync()); Assert.Single(await db.Notifications.Where(n => n.SourceEventId == r.SourceEventId).ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentDifferentEventsSameBidTransition_OnlyOneCanAccept()
    {
        var r = Request(); var statuses = await Task.WhenAll(new[] { r,r with { SourceEventId = Guid.NewGuid() } }.Select(async x =>
        { try { await Accept(x); return 200; } catch(DomainException ex) { Assert.Equal("AUCTION_BID_ALREADY_NOTIFIED",ex.ErrorCode); return ex.StatusCode; } }));
        Assert.Single(statuses,x => x == 200); Assert.Single(statuses,x => x == 409);
    }

    [PostgresFact]
    public async Task SamePreviousBidWithNewTrigger_CannotNotifyPreviousLossAgain()
    {
        var r = Request(); await Accept(r);
        await using(var db = Db()) await db.AuctionOutbidDispatches.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted,true));
        await Check(await _client.PostAsJsonAsync(Route,r with { SourceEventId = Guid.NewGuid(),TriggerBidId = Guid.NewGuid() }),HttpStatusCode.Conflict,"AUCTION_BID_ALREADY_NOTIFIED");
    }

    [PostgresFact]
    public async Task NewValidBidPairs_CanNotifySameUserAgainInSameAuction()
    {
        var r = Request(); await Accept(r); await Accept(r with { SourceEventId = Guid.NewGuid(),TriggerBidId = Guid.NewGuid(),PreviousBidId = Guid.NewGuid(),NewAmount = 130000 });
        Assert.Equal(2,(await Inbox(r.PreviousHighestBidderUserId!.Value)).Pagination.TotalCount);
    }

    [PostgresFact]
    public async Task AuditFailure_RollsBackReceiptNotificationDeliveriesAndBidReservation()
    {
        var r = Request(); await using(var db = Db(new FailAllSave())) await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(r));
        await using var verify = Db(); Assert.Empty(await verify.AuctionOutbidDispatches.ToListAsync()); Assert.False(await verify.Notifications.AnyAsync(n => n.SourceEventId == r.SourceEventId));
        Assert.False((await Accept(r)).IsReplay);
    }

    [PostgresFact]
    public async Task InvalidTemplateOrMissingChannel_CannotReserveBid()
    {
        var r = Request(); await using var db = Db();
        await db.NotificationTemplates.Where(t => t.Code == "AUCTION_BID_OUTBID_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.ChannelCode,ChannelType.Email));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"AUCTION_OUTBID_TEMPLATE_INVALID");
        await db.NotificationTemplates.Where(t => t.Code == "AUCTION_BID_OUTBID_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.ChannelCode,ChannelType.Push).SetProperty(t => t.IsActive,false));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.NotFound,"TEMPLATE_NOT_FOUND");
        await db.NotificationTemplates.Where(t => t.Code == "AUCTION_BID_OUTBID_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,true));
        await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted,true));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"CHANNEL_NOT_FOUND"); Assert.Empty(await db.AuctionOutbidDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task MissingTemplateDataAndMalformedPlaceholder_DoNotCreateHalfAcceptedNotice()
    {
        var r = Request(); await using var db = Db();
        await db.NotificationTemplates.Where(t => t.Code == "AUCTION_BID_OUTBID_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{Token}}"));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.BadRequest,"TEMPLATE_DATA_MISSING");
        await db.NotificationTemplates.Where(t => t.Code == "AUCTION_BID_OUTBID_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{invalid"));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"TEMPLATE_INVALID"); Assert.Empty(await db.AuctionOutbidDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task GenericDispatchKeyCollision_IsRejectedInsteadOfAdoptingUnknownContent()
    {
        var r = Request(); await using(var db = Db()) await new DispatchService(db).CreateAsync(new()
        {
            SourceEventId = r.SourceEventId,UserId = r.PreviousHighestBidderUserId,TemplateCode = "AUCTION_BID_OUTBID_PUSH",
            Data = new() { ["AuctionTitle"] = "Different",["NewAmount"] = "Different",["OccurredAtUtc"] = "Different" }
        });
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"AUCTION_OUTBID_CONFLICT");
        await using var verify = Db(); Assert.Empty(await verify.AuctionOutbidDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task HistoricalOutbidAfterEnd_UsesOriginalEffectiveTimeWithoutLiveBidClaim()
    {
        var r = Request() with { OccurredAt = _clock.GetUtcNow().AddDays(-2),AuctionEndsAt = _clock.GetUtcNow().AddDays(-1) };
        await Accept(r); var item = Assert.Single((await Inbox(r.PreviousHighestBidderUserId!.Value)).Items);
        Assert.Contains("ghi nhận lúc",item.Body); using var payload = JsonDocument.Parse(item.Payload!);
        Assert.Equal(new DateTimeOffset(r.AuctionEndsAt!.Value.UtcTicks / 10 * 10,TimeSpan.Zero),payload.RootElement.GetProperty("auctionEndsAt").GetDateTimeOffset());
    }

    [PostgresFact]
    public async Task SwaggerAndOptionalTemplateSeed_MatchContract()
    {
        var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route,swagger); Assert.Contains("{notificationId}",swagger); Assert.Contains("previousHighestBidderUserId",swagger);
        await using var db = Db(); Assert.False((await db.NotificationTemplates.SingleAsync(t => t.Code == "AUCTION_BID_OUTBID_PUSH")).IsMandatory);
        Assert.False((await db.NotificationTemplates.SingleAsync(t => t.Code == "AUCTION_OUTBID_PUSH")).IsMandatory);
    }

    [PostgresFact]
    public async Task SameTriggerWithDifferentPreviousBid_CannotGenerateASecondLossNotice()
    {
        var r = Request(); await Accept(r);
        await Check(await _client.PostAsJsonAsync(Route,r with { SourceEventId = Guid.NewGuid(),PreviousBidId = Guid.NewGuid(),PreviousHighestBidderUserId = Guid.NewGuid() }),
            HttpStatusCode.Conflict,"AUCTION_BID_ALREADY_NOTIFIED");
        await using var verify = Db(); Assert.Single(await verify.AuctionOutbidDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task TemplateSharedLock_PreventsConcurrentTypeMutationDuringAcceptance()
    {
        var r = Request(); var gate = new GateSave(); await using var acceptingDb = Db(gate);
        var accepting = Service(acceptingDb).CreateAsync(r); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var editor = Db(); await using var tx = await editor.Database.BeginTransactionAsync();
            await editor.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => editor.NotificationTemplates.Where(t => t.Code == "AUCTION_BID_OUTBID_PUSH")
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.NotificationType,NotificationType.OrderPaid)));
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
        db.NotificationPreferences.Add(new() { Id = Guid.NewGuid(),UserId = owner,NotificationType = NotificationType.AuctionOutbid,ChannelCode = channel,IsDisabled = disabled });
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
