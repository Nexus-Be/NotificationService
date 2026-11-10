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
using Notification.Application.Services.ShipmentEvents;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class ShipmentEventApiIntegrationTests : IAsyncLifetime
{
    private const string Route = "/api/v1/notifications/shipment-events";
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
    private ShipmentEventService Service(AppDbContext db) => new(db,_clock);
    private CreateShipmentEventRequest Request(string status = "Created",bool auction = false,long version = 1)
    {
        var created=_clock.GetUtcNow().AddHours(-2);
        return new() { SourceEventId=Guid.NewGuid(),ShipmentId=Guid.NewGuid(),SourceVersion=version,OrderId=Guid.NewGuid(),ShopId=Guid.NewGuid(),BuyerUserId=Guid.NewGuid(),SellerUserId=Guid.NewGuid(),OrderKind=auction ? "Auction" : "Regular",AuctionId=auction ? Guid.NewGuid() : null,OrderNo="ORDER-<b>DEMO</b>",Carrier="GHN",TrackingCode="GHN-TEST_001",Status=status,Reason=NeedsReason(status) ? "Source user-safe reason" : null,PaidAt=created.AddHours(-2),SellerAcceptedAt=created.AddHours(-1),ShipmentCreatedAt=created,OccurredAt=status=="Created" ? created : created.AddMinutes(version) };
    }
    private static bool NeedsReason(string status) => status is "DeliveryFailed" or "Returning" or "Returned" or "Cancelled";
    private static CreateShipmentEventRequest Next(CreateShipmentEventRequest r,string status,long version) => r with { SourceEventId=Guid.NewGuid(),SourceVersion=version,Status=status,Reason=NeedsReason(status) ? "Source user-safe reason" : null,OccurredAt=status=="Created" ? r.ShipmentCreatedAt : r.ShipmentCreatedAt!.Value.AddMinutes(version) };
    private async Task<ShipmentEventResponse> Accept(CreateShipmentEventRequest request)
    { await using var db = Db(); return await Service(db).CreateAsync(request); }
    private async Task<bool> Process()
    { await using var db = Db(); return await new DeliveryService(db,_sender,new(),_clock,_push).ProcessNextAsync(); }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_shipment_event_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(ShipmentEventsController).Assembly)
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
    public async Task NineStatesBothKinds_NotifyPrivateBuyerSellerItemsWithoutFinancialClaims()
    {
        var states=Enum.GetNames<ShipmentNotificationStatus>();
        foreach(var auction in new[] { false,true }) foreach(var status in states)
        {
            Role("service"); var r=Request(status,auction); await Device(r.BuyerUserId!.Value); await Device(r.SellerUserId!.Value); var response=await _client.PostAsJsonAsync(Route,r); await Check(response,HttpStatusCode.OK); Assert.True(response.Headers.CacheControl!.NoStore); var result=(await response.Content.ReadFromJsonAsync<ShipmentEventResponse>())!; Assert.Null(result.SuppressionReason); Assert.NotNull(result.Buyer); Assert.NotNull(result.Seller);
            Assert.True(await Process()); Assert.True(await Process()); var buyer=Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); var seller=Assert.Single((await Inbox(r.SellerUserId.Value)).Items); Assert.DoesNotContain(r.SellerUserId.ToString()!,buyer.Payload); Assert.DoesNotContain(r.BuyerUserId.ToString()!,seller.Payload); Assert.Contains(r.TrackingCode,buyer.Body); Assert.DoesNotContain("đã hoàn tiền",buyer.Body); Assert.DoesNotContain("đã giải ngân",seller.Body); Assert.DoesNotContain("đã nhả kho",seller.Body); Assert.DoesNotContain("COMPLETED",buyer.Body);
            using var p=JsonDocument.Parse(buyer.Payload!); Assert.Equal(status,p.RootElement.GetProperty("status").GetString()); Assert.Equal(r.SourceVersion,p.RootElement.GetProperty("sourceVersion").GetInt64()); Assert.Equal("OrderShipped",p.RootElement.GetProperty("type").GetString()); Assert.Equal("Buyer",p.RootElement.GetProperty("audience").GetString());
        }
        Assert.Equal(36,_push.Messages.Count); Assert.Empty(_sender.Messages); await using var db=Db(); Assert.Equal(18,await db.ShipmentNotificationStreams.CountAsync()); Assert.Equal(18,await db.ShipmentEventDispatches.CountAsync()); Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task InvalidSourceTimelineStatusKindCarrierAndReason_AreRejectedWithoutStream()
    {
        var r=Request(); foreach(var invalid in new[] { r with { SourceEventId=Guid.Empty },r with { ShipmentId=null },r with { OrderId=Guid.Empty },r with { ShopId=Guid.Empty },r with { BuyerUserId=r.SellerUserId },r with { SourceVersion=0 },r with { SourceVersion=-1 },r with { Status="Unknown" },r with { Status="0" },r with { Carrier="Other" },r with { TrackingCode="https://example.com/track" },r with { TrackingCode="bad\n" },r with { OrderNo="bad\n" },r with { TraceId="\n" },r with { OrderKind="Other" },r with { AuctionId=Guid.NewGuid() },Request(auction:true) with { AuctionId=null },r with { Reason="Unexpected" },Request("DeliveryFailed") with { Reason=" " },Request("Returned") with { Reason="bad\n" },r with { PaidAt=null },r with { SellerAcceptedAt=r.PaidAt!.Value.AddSeconds(-1) },r with { ShipmentCreatedAt=r.SellerAcceptedAt!.Value.AddSeconds(-1) },r with { OccurredAt=r.ShipmentCreatedAt!.Value.AddSeconds(-1) },r with { OccurredAt=r.ShipmentCreatedAt!.Value.AddSeconds(1) },Request("InTransit") with { OccurredAt=_clock.GetUtcNow().AddMinutes(1) } }) await Check(await _client.PostAsJsonAsync(Route,invalid),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await using var db=Db(); Assert.Empty(await db.ShipmentNotificationStreams.ToListAsync()); Assert.Empty(await db.ShipmentEventDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task AuthStrictPrivateFieldsAndBodyLimit_OnlyTrustedSourceCanAccept()
    {
        var r=Request(); Role(null); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Unauthorized); foreach(var role in new[] { "buyer","seller","staff" }) { Role(role); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Forbidden); }
        Role("service"); foreach(var field in new[] { "address","phone","coordinates","carrierPayload","trackingUrl","token","password","otpCode","refundAmount","walletCredited","stockReleased" }) { var b=JsonSerializer.SerializeToNode(r)!.AsObject(); b[field]="private"; await Check(await _client.PostAsync(Route,new StringContent(b.ToJsonString(),Encoding.UTF8,"application/json")),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT"); }
        await Check(await _client.PostAsJsonAsync(Route,r with { OrderNo=new string('x',17000) }),HttpStatusCode.RequestEntityTooLarge,"REQUEST_BODY_TOO_LARGE"); Role("admin"); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task CanonicalReplay_KeepsGroupsAndFanoutWithoutRequiringTemplates()
    {
        var r=Request("DeliveryFailed",true); await Device(r.BuyerUserId!.Value); var first=await Accept(r); await Device(r.BuyerUserId.Value);
        await using(var db=Db()) await db.NotificationTemplates.Where(t=>t.Code=="SHIPMENT_DELIVERY_FAILED_BUYER_PUSH").ExecuteUpdateAsync(s=>s.SetProperty(t=>t.IsActive,false));
        var replay=await Accept(r with { Status="deliveryfailed",OrderKind="auction",Carrier="ghn",TrackingCode=r.TrackingCode.ToLowerInvariant(),OrderNo=" "+r.OrderNo+" ",Reason=" "+r.Reason+" ",PaidAt=r.PaidAt!.Value.ToOffset(TimeSpan.FromHours(7)),SellerAcceptedAt=r.SellerAcceptedAt!.Value.ToOffset(TimeSpan.FromHours(7)),ShipmentCreatedAt=r.ShipmentCreatedAt!.Value.ToOffset(TimeSpan.FromHours(7)),OccurredAt=r.OccurredAt!.Value.ToOffset(TimeSpan.FromHours(7)),TraceId="retry" }); Assert.True(replay.IsReplay); Assert.Equal(first.Buyer!.NotificationId,replay.Buyer!.NotificationId); Assert.Equal(first.Buyer.Deliveries.Select(x=>x.Id),replay.Buyer.Deliveries.Select(x=>x.Id));
    }

    [PostgresFact]
    public async Task ImmutableHeaderAndSameEventProof_CannotChangeRecipientsTrackingOrOrder()
    {
        var r=Request(); await Accept(r);
        foreach(var changed in new[] { r with { BuyerUserId=Guid.NewGuid() },r with { SourceVersion=2 },r with { OrderNo="changed" } }) { var e=await Assert.ThrowsAsync<DomainException>(()=>Accept(changed)); Assert.Equal("SHIPMENT_EVENT_CONFLICT",e.ErrorCode); }
        var next=Next(r,"InTransit",2); foreach(var changed in new[] { next with { BuyerUserId=Guid.NewGuid() },next with { SellerUserId=Guid.NewGuid() },next with { OrderId=Guid.NewGuid() },next with { TrackingCode="OTHER" },next with { ShipmentCreatedAt=r.ShipmentCreatedAt!.Value.AddSeconds(-1) } }) { var e=await Assert.ThrowsAsync<DomainException>(()=>Accept(changed)); Assert.Equal("SHIPMENT_EVENT_CONFLICT",e.ErrorCode); }
        await using var db=Db(); Assert.Equal(1,(await db.ShipmentNotificationStreams.SingleAsync()).LastVersion); Assert.Single(await db.ShipmentEventDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task HiddenReceiptVersionAliases_ConflictAndReplayDoesNotResurrect()
    {
        var r=Request(); await Accept(r); await using(var db=Db()) { await db.ShipmentEventDispatches.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true)); await db.ShipmentNotificationStreams.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true)); }
        Assert.True((await Accept(r)).IsReplay); var e=await Assert.ThrowsAsync<DomainException>(()=>Accept(r with { SourceEventId=Guid.NewGuid() })); Assert.Equal("SHIPMENT_VERSION_ALREADY_NOTIFIED",e.ErrorCode);
        Assert.Null((await Accept(Next(r,"PickedUp",2))).SuppressionReason); await using var verify=Db(); var stream=await verify.ShipmentNotificationStreams.IgnoreQueryFilters().SingleAsync(); Assert.True(stream.IsDeleted); Assert.Equal(2,stream.LastVersion);
    }

    [PostgresFact]
    public async Task NewerDeliveredThenOldCreated_IsDurablySuppressedWithoutTemplatesOrGroups()
    {
        var initial=Request(); var latest=Next(initial,"Delivered",5); await Accept(latest);
        await using(var db=Db()) await db.NotificationTemplates.Where(t=>t.Code.StartsWith("SHIPMENT_CREATED")).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.IsActive,false));
        var stale=await Accept(initial); Assert.Equal("SHIPMENT_EVENT_STALE",stale.SuppressionReason); Assert.Null(stale.Buyer); Assert.Null(stale.Seller); Assert.True((await Accept(initial)).IsReplay); Assert.Single((await Inbox(initial.BuyerUserId!.Value)).Items);
        await using var dbCheck=Db(); var stream=await dbCheck.ShipmentNotificationStreams.SingleAsync(); Assert.Equal(5,stream.LastVersion); Assert.Equal(ShipmentNotificationStatus.Delivered,stream.LastStatus); Assert.Equal(2,await dbCheck.ShipmentEventDispatches.CountAsync());
    }

    [PostgresFact]
    public async Task RepeatedStatusAdvancesWatermarkWithoutSpam_RedeliveryStatusIsNotRanked()
    {
        var r=Request("InTransit",version:2); await Accept(r); var duplicate=Next(r,"InTransit",4); var skipped=await Accept(duplicate); Assert.Equal("SHIPMENT_STATUS_UNCHANGED",skipped.SuppressionReason); Assert.Null(skipped.Buyer);
        Assert.Equal("SHIPMENT_EVENT_STALE",(await Accept(Next(r,"PickedUp",3))).SuppressionReason);
        await Accept(Next(r,"DeliveryFailed",5)); await Accept(Next(r,"OutForDelivery",6)); Assert.Equal(3,(await Inbox(r.BuyerUserId!.Value)).Items.Count); await using var db=Db(); Assert.Equal(6,(await db.ShipmentNotificationStreams.SingleAsync()).LastVersion);
    }

    [PostgresFact]
    public async Task HigherVersionWithBackwardTime_ConflictsWithoutAdvancingStream()
    {
        var r=Request("InTransit",version:3); await Accept(r); var next=Next(r,"OutForDelivery",4) with { OccurredAt=r.OccurredAt!.Value.AddSeconds(-1) }; var e=await Assert.ThrowsAsync<DomainException>(()=>Accept(next)); Assert.Equal("SHIPMENT_EVENT_CONFLICT",e.ErrorCode); await using var db=Db(); Assert.Equal(3,(await db.ShipmentNotificationStreams.SingleAsync()).LastVersion);
    }

    [PostgresFact]
    public async Task ConcurrentOutOfOrderVersions_EndAtNewestAndNeverRegress()
    {
        var r=Request(); var replies=await Task.WhenAll(new[] { r,Next(r,"Delivered",5) }.Select(Accept)); Assert.Contains(replies,x=>x.SuppressionReason is null);
        var stale=await Accept(Next(r,"InTransit",3)); Assert.Equal("SHIPMENT_EVENT_STALE",stale.SuppressionReason); await using var db=Db(); var stream=await db.ShipmentNotificationStreams.SingleAsync(); Assert.Equal(5,stream.LastVersion); Assert.Equal(ShipmentNotificationStatus.Delivered,stream.LastStatus);
    }

    [PostgresFact]
    public async Task ConcurrentVersionAliases_OneReceiptAndOnePairOnly()
    {
        var r=Request(); var statuses=await Task.WhenAll(new[] { r,r with { SourceEventId=Guid.NewGuid() } }.Select(async x=> { try { await Accept(x); return 200; } catch(DomainException e) { Assert.Equal("SHIPMENT_VERSION_ALREADY_NOTIFIED",e.ErrorCode); return e.StatusCode; } })); Assert.Single(statuses,x=>x==200); Assert.Single(statuses,x=>x==409);
    }

    [PostgresFact]
    public async Task TemplateFailureAndPersistenceFailure_RollBackExistingStreamWatermark()
    {
        var r=Request(); await Accept(r); var next=Next(r,"PickedUp",2); string? body;
        await using(var db=Db()) { body=(await db.NotificationTemplates.SingleAsync(t=>t.Code=="SHIPMENT_PICKED_UP_SELLER_PUSH")).BodyTemplate; await db.NotificationTemplates.Where(t=>t.Code=="SHIPMENT_PICKED_UP_SELLER_PUSH").ExecuteUpdateAsync(s=>s.SetProperty(t=>t.BodyTemplate,"{{PrivateAddress}}")); }
        await Check(await _client.PostAsJsonAsync(Route,next),HttpStatusCode.BadRequest,"TEMPLATE_DATA_MISSING"); await using(var db=Db()) { Assert.Equal(1,(await db.ShipmentNotificationStreams.SingleAsync()).LastVersion); Assert.Single(await db.ShipmentEventDispatches.ToListAsync()); await db.NotificationTemplates.Where(t=>t.Code=="SHIPMENT_PICKED_UP_SELLER_PUSH").ExecuteUpdateAsync(s=>s.SetProperty(t=>t.BodyTemplate,body)); }
        await using(var db=Db(new FailAllSave())) await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(db).CreateAsync(next)); await using(var db=Db()) Assert.Equal(1,(await db.ShipmentNotificationStreams.SingleAsync()).LastVersion);
        Assert.Null((await Accept(next)).SuppressionReason);
    }

    [PostgresFact]
    public async Task OptionalPreferencesAndOwnMandatoryPair_RespectEachChannelAndGlobalDisable()
    {
        var r=Request(); await Device(r.BuyerUserId!.Value); await using(var db=Db()) { foreach(var c in new[] { ChannelType.Push,ChannelType.InApp }) db.NotificationPreferences.Add(new() { Id=Guid.NewGuid(),UserId=r.BuyerUserId.Value,NotificationType=NotificationType.OrderShipped,ChannelCode=c,IsDisabled=true }); await db.SaveChangesAsync(); }
        var first=await Accept(r); Assert.False(first.Buyer!.InboxVisible); Assert.All(first.Buyer.Deliveries,d=>Assert.Equal("PREFERENCE_DISABLED",d.ErrorCode)); Assert.False(await Process());
        await using(var db=Db()) await db.NotificationTemplates.Where(t=>t.Code=="SHIPMENT_PICKED_UP_BUYER_PUSH").ExecuteUpdateAsync(s=>s.SetProperty(t=>t.IsMandatory,true));
        var next=await Accept(Next(r,"PickedUp",2)); Assert.False(next.Buyer!.InboxVisible); Assert.Contains(next.Buyer.Deliveries,d=>d.Channel=="Push" && d.Status=="Pending"); Assert.True(await Process());
        await using(var db=Db()) await db.NotificationChannels.Where(c=>c.Code==ChannelType.Push).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.IsDisabled,true)); Assert.Contains((await Accept(Next(r,"InTransit",3))).Buyer!.Deliveries,d=>d.ErrorCode=="CHANNEL_DISABLED");
    }

    [PostgresFact]
    public async Task MissingInvalidTemplatesAndChannel_DoNotLeavePartialPairOrStream()
    {
        await using(var db=Db()) await db.NotificationTemplates.Where(t=>t.Code=="SHIPMENT_CREATED_BUYER_PUSH").ExecuteUpdateAsync(s=>s.SetProperty(t=>t.IsActive,false)); await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.NotFound,"TEMPLATE_NOT_FOUND");
        await using(var db=Db()) await db.NotificationTemplates.Where(t=>t.Code=="SHIPMENT_CREATED_BUYER_PUSH").ExecuteUpdateAsync(s=>s.SetProperty(t=>t.IsActive,true).SetProperty(t=>t.ChannelCode,ChannelType.Email)); await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.Conflict,"SHIPMENT_EVENT_TEMPLATE_INVALID");
        await using(var db=Db()) await db.NotificationTemplates.Where(t=>t.Code=="SHIPMENT_CREATED_BUYER_PUSH").ExecuteUpdateAsync(s=>s.SetProperty(t=>t.ChannelCode,ChannelType.Push).SetProperty(t=>t.BodyTemplate,"{{OrderNo")); await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.Conflict,"TEMPLATE_INVALID");
        await using(var db=Db()) await db.NotificationChannels.Where(c=>c.Code==ChannelType.InApp).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.IsDeleted,true)); await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.Conflict,"CHANNEL_NOT_FOUND"); await using var verify=Db(); Assert.Empty(await verify.ShipmentNotificationStreams.ToListAsync()); Assert.Empty(await verify.ShipmentEventDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task GenericCollisionAndCorruptedManifest_DoNotAdoptUnknownNotifications()
    {
        var r=Request(); await using(var db=Db()) await new DispatchService(db).CreateAsync(new() { SourceEventId=r.SourceEventId,UserId=r.BuyerUserId,TemplateCode="SHIPMENT_CREATED_BUYER_PUSH",Data=new() { ["OrderNo"]="Other",["TrackingCode"]="Other",["OccurredAtUtc"]="Other" } }); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"SHIPMENT_EVENT_CONFLICT"); await using(var db=Db()) Assert.Empty(await db.ShipmentNotificationStreams.ToListAsync());
        var other=Request(); var accepted=await Accept(other); await using(var db=Db()) { await db.NotificationLogs.Where(l=>l.NotificationId==accepted.Seller!.NotificationId).ExecuteDeleteAsync(); await db.NotificationDeliveries.Where(d=>d.NotificationId==accepted.Seller!.NotificationId).ExecuteDeleteAsync(); } await Check(await _client.PostAsJsonAsync(Route,other),HttpStatusCode.Conflict,"DELIVERY_STATE_INVALID");
    }

    [PostgresFact]
    public async Task HistoricalCanonicalReplayAndFreshShipment_KeepSourceTimelineAndSeparateHistory()
    {
        var r=Request("Delivered") with { PaidAt=_clock.GetUtcNow().AddDays(-5),SellerAcceptedAt=_clock.GetUtcNow().AddDays(-4),ShipmentCreatedAt=_clock.GetUtcNow().AddDays(-3),OccurredAt=_clock.GetUtcNow().AddDays(-2) }; await Accept(r); var item=Assert.Single((await Inbox(r.BuyerUserId!.Value)).Items); using var p=JsonDocument.Parse(item.Payload!); Assert.Equal(r.OccurredAt!.Value.UtcTicks/10*10,p.RootElement.GetProperty("occurredAt").GetDateTimeOffset().UtcTicks);
        await Accept(Request() with { OrderId=r.OrderId,BuyerUserId=r.BuyerUserId,SellerUserId=r.SellerUserId }); Assert.Equal(2,(await Inbox(r.BuyerUserId.Value)).Items.Count);
    }

    [PostgresFact]
    public async Task PriorSellerDecisionHistory_RemainsSeparateFromShipmentEvents()
    {
        var r=Request(); await using(var db=Db()) await new Notification.Application.Services.OrderDecisions.OrderDecisionService(db,_clock).CreateAsync(new() { SourceEventId=Guid.NewGuid(),OrderId=r.OrderId,ShopId=r.ShopId,BuyerUserId=r.BuyerUserId,SellerUserId=r.SellerUserId,OrderKind=r.OrderKind,AuctionId=r.AuctionId,OrderNo=r.OrderNo,Outcome="Accepted",OrderStatus="Shipping",PaidAt=r.PaidAt,SellerAcceptStartedAt=r.PaidAt,SellerAcceptDeadline=r.PaidAt!.Value.AddHours(24),DecidedAt=r.SellerAcceptedAt });
        await Accept(r); Assert.Equal(2,(await Inbox(r.BuyerUserId!.Value)).Items.Count); Assert.Equal(2,(await Inbox(r.SellerUserId!.Value)).Items.Count);
    }

    [PostgresFact]
    public async Task SnapshotRetry_DoesNotRewriteOldShipmentStatusAfterLaterTransition()
    {
        var r=Request(); await Device(r.BuyerUserId!.Value); await Accept(r); _push.Result=_=>PushDeliveryResult.Failure("FCM_UNAVAILABLE","Test transient failure.",true); Assert.True(await Process()); var oldBody=Assert.Single(_push.Messages).Body;
        await Accept(Next(r,"PickedUp",2)); await using(var db=Db()) await db.NotificationTemplates.Where(t=>t.Code=="SHIPMENT_CREATED_BUYER_PUSH").ExecuteUpdateAsync(s=>s.SetProperty(t=>t.BodyTemplate,"Changed later")); _clock.Advance(TimeSpan.FromSeconds(10)); _push.Result=_=>PushDeliveryResult.Sent("ok"); for(var i=0;i<2;i++) Assert.True(await Process()); Assert.Equal(2,_push.Messages.Count(m=>m.Body==oldBody)); Assert.Equal(2,(await Inbox(r.BuyerUserId.Value)).Items.Count);
    }

    [PostgresFact]
    public async Task SwaggerAndPreferenceMatrix_ReuseOptionalOrderShippedType()
    {
        Role("buyer"); var prefs=(await _client.GetFromJsonAsync<List<NotificationPreferenceResponse>>("/api/v1/notifications/preferences"))!; Assert.Equal(54,prefs.Count); Assert.Equal(18,prefs.Count(p=>p.IsMandatory)); Assert.All(prefs.Where(p=>p.NotificationType=="OrderShipped"),p=>Assert.False(p.IsMandatory)); var swagger=await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route,swagger); Assert.Contains("sourceVersion",swagger); Assert.Contains("suppressionReason",swagger); Assert.Equal(25,(int)AuditAction.ShipmentEventAccepted); Assert.Equal(8,(int)ShipmentNotificationStatus.Cancelled);
    }

    [PostgresFact]
    public async Task NoDevices_StillCreatesTwoUnreadInboxItemsAndSkippedPushMarkers()
    {
        var r = Request(); var result = await Accept(r);
        Assert.Equal("Completed",result.Buyer!.NotificationStatus); Assert.True(result.Buyer!.InboxVisible);
        Assert.Contains(result.Buyer!.Deliveries,d => d.Channel == "Push" && d.ErrorCode == "PUSH_NO_ACTIVE_DEVICES");
        Assert.Equal(1,(await Inbox(r.BuyerUserId!.Value,true)).Pagination.TotalCount); Assert.Equal(1,(await Inbox(r.SellerUserId!.Value,true)).Pagination.TotalCount);
        Assert.False(await Process());
    }

    [PostgresFact]
    public async Task PushFanout_UsesSameLogicalId_AndDoesNotMultiplyBellItems()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value,2); await Device(r.SellerUserId!.Value,3); await Device(Guid.NewGuid());
        var accepted = await Accept(r); for(var i = 0;i < 5;i++) Assert.True(await Process()); Assert.False(await Process());
        Assert.Equal(5,_push.Messages.Count); Assert.All(_push.Messages,m => Assert.True(m.NotificationId == accepted.Buyer!.NotificationId || m.NotificationId == accepted.Seller!.NotificationId));
        Assert.Equal(2,_push.Messages.Count(m => m.UserId == r.BuyerUserId)); Assert.Equal(3,_push.Messages.Count(m => m.UserId == r.SellerUserId));
        Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); Assert.Single((await Inbox(r.SellerUserId.Value)).Items);
        var replay = await Accept(r); Assert.Equal("Completed",replay.Buyer!.NotificationStatus); Assert.Equal("Completed",replay.Seller!.NotificationStatus);
    }

    [PostgresFact]
    public async Task BuyerPushFailure_DoesNotLoseEitherInboxOrSellerPush()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await Device(r.SellerUserId!.Value);
        _push.Result = m => m.UserId == r.BuyerUserId ? PushDeliveryResult.Failure("FCM_AUTH_FAILED","Test permanent failure.") : PushDeliveryResult.Sent("seller-ok");
        await Accept(r); await Process(); await Process(); var replay = await Accept(r);
        Assert.Equal("Partial",replay.Buyer!.NotificationStatus); Assert.Equal("Completed",replay.Seller!.NotificationStatus);
        Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); Assert.Single((await Inbox(r.SellerUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task DisabledPush_PreservesInboxAndNeverRequeuesOnReplayAfterEnable()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var result = await Accept(r); Assert.Contains(result.Buyer!.Deliveries,d => d.Channel == "Push" && d.Status == "Skipped");
        Assert.Single((await Inbox(r.BuyerUserId.Value)).Items);
        await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,false));
        Assert.True((await Accept(r)).IsReplay); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task DisabledInApp_PushStillRuns_AndBellReadActionsCannotResurrectItem()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await Device(r.SellerUserId!.Value);
        await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var accepted = await Accept(r); Assert.False(accepted.Buyer!.InboxVisible); Assert.Empty((await Inbox(r.BuyerUserId.Value)).Items);
        await Check(await _client.GetAsync(Route + "/" + accepted.Buyer!.NotificationId),HttpStatusCode.OK);
        await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + accepted.Buyer!.NotificationId + "/read-states",new { isRead = false }),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
        await Process(); await Process(); Assert.Equal(2,_push.Messages.Count); Assert.Empty((await Inbox(r.BuyerUserId.Value,true)).Items);
        await using var dbCheck = Db(); var row = await dbCheck.Notifications.SingleAsync(n => n.Id == accepted.Buyer!.NotificationId);
        Assert.False(row.IsInboxVisible); Assert.False(row.IsDeleted); Assert.True(row.IsRead);
    }

    [PostgresFact]
    public async Task BothChannelsDisabled_RecordsSuppressionWithoutVisibleInboxOrProviderCall()
    {
        var r = Request(); await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push || c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var accepted = await Accept(r); Assert.False(accepted.Buyer!.InboxVisible); Assert.All(accepted.Buyer!.Deliveries,d => Assert.Equal("Skipped",d.Status));
        Assert.Empty((await Inbox(r.BuyerUserId!.Value)).Items); Assert.False(await Process());
        await Check(await _client.GetAsync(Route + "/" + accepted.Buyer!.NotificationId),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
    }

    [PostgresFact]
    public async Task DeviceOverflow_SkipsOnlyAffectedPushBranch_AndKeepsBothInboxes()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value,101); await Device(r.SellerUserId!.Value);
        var accepted = await Accept(r); Assert.Contains(accepted.Buyer!.Deliveries,d => d.ErrorCode == "PUSH_DEVICE_LIMIT");
        Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); Assert.Single((await Inbox(r.SellerUserId.Value)).Items);
        Assert.True(await Process()); Assert.False(await Process()); Assert.Equal(r.SellerUserId,Assert.Single(_push.Messages).UserId);
    }

    [PostgresFact]
    public async Task OwnerGetAndReadStates_ProtectRolesBadgeAndIndependentBranches()
    {
        var r = Request(); var result = await Accept(r); Role(null);
        await Check(await _client.GetAsync(Route + "/" + result.Buyer!.NotificationId),HttpStatusCode.Unauthorized);
        Role("buyer",r.BuyerUserId); await Check(await _client.GetAsync(Route + "/" + result.Seller!.NotificationId),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
        var detail = await _client.GetAsync(Route + "/" + result.Buyer!.NotificationId); await Check(detail,HttpStatusCode.OK); Assert.True(detail.Headers.CacheControl!.NoStore);
        await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + result.Buyer!.NotificationId + "/read-states",new { isRead = true }),HttpStatusCode.OK);
        Assert.Empty((await Inbox(r.BuyerUserId!.Value,true)).Items); Assert.Single((await Inbox(r.SellerUserId!.Value,true)).Items);
        Role("buyer",r.BuyerUserId); await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + result.Buyer!.NotificationId + "/read-states",new { isRead = false }),HttpStatusCode.OK);
        Assert.Single((await Inbox(r.BuyerUserId.Value,true)).Items);
        await using var db = Db(); Assert.All(await db.NotificationDeliveries.Where(d => d.NotificationId == result.Buyer!.NotificationId && d.ChannelCode == ChannelType.InApp).ToListAsync(),d => Assert.Equal(DeliveryStatus.Sent,d.Status));
    }

    [PostgresFact]
    public async Task ConcurrentSameEvent_CreatesTwoItemsOnceAndReplaysSevenTimes()
    {
        var r = Request(); var results = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Accept(r))).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results,x => !x.IsReplay); Assert.Single(results.Select(x => x.Buyer!.NotificationId).Distinct());
        await using var db = Db(); Assert.Equal(2,await db.Notifications.CountAsync(n => n.SourceEventId == r.SourceEventId)); Assert.Single(await db.ShipmentEventDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task AuditWriteFailure_RollsBackBothBranchesAllDeliveriesAndReceipt()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value);
        await using(var db = Db(new FailAllSave())) await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(r));
        await using var verify = Db(); Assert.Empty(await verify.ShipmentEventDispatches.ToListAsync()); Assert.False(await verify.Notifications.AnyAsync(n => n.SourceEventId == r.SourceEventId));
        Assert.Empty(await verify.AuditLogs.Where(a => a.Action == AuditAction.ShipmentEventAccepted).ToListAsync()); Assert.False(await Process()); Assert.False((await Accept(r)).IsReplay);
    }

    [PostgresFact]
    public async Task TransientPushRetry_DoesNotResendSuccessfulDeviceOrCreateNewInbox()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await Device(r.SellerUserId!.Value); var failures = 0;
        _push.Result = m => m.UserId == r.BuyerUserId && failures++ == 0 ? PushDeliveryResult.Failure("FCM_UNAVAILABLE","Test retry.",true) : PushDeliveryResult.Sent("ok");
        await Accept(r); await Process(); await Process(); Assert.False(await Process()); _clock.Advance(TimeSpan.FromSeconds(10)); Assert.True(await Process());
        Assert.Equal(3,_push.Messages.Count); Assert.Single(_push.Messages,m => m.UserId == r.SellerUserId); Assert.Single((await Inbox(r.BuyerUserId!.Value)).Items);
    }

    [PostgresFact]
    public async Task RevokedDevice_IsSkippedWithoutLosingInboxOrSendingToNewOwner()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await Accept(r);
        await using(var db = Db()) await db.DeviceTokens.Where(d => d.UserId == r.BuyerUserId).ExecuteUpdateAsync(s => s.SetProperty(d => d.UserId,Guid.NewGuid()));
        Assert.True(await Process()); Assert.Empty(_push.Messages); Assert.Single((await Inbox(r.BuyerUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task TemplateSharedLock_PreventsMetadataEditUntilBothBranchesAreSaved()
    {
        var r = Request(); var gate = new GateSave(); await using var acceptingDb = Db(gate);
        var accepting = Service(acceptingDb).CreateAsync(r); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var editor = Db(); await using var tx = await editor.Database.BeginTransactionAsync();
            await editor.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => editor.NotificationTemplates.Where(t => t.Code == "SHIPMENT_CREATED_SELLER_PUSH")
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.NotificationType,NotificationType.Promotion)));
            Assert.Equal("55P03",Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        finally { gate.Release.TrySetResult(); }
        Assert.False((await accepting.WaitAsync(TimeSpan.FromSeconds(5))).IsReplay);
    }

    [PostgresFact]
    public async Task LegacyRowsRemainVisibleAndSwaggerExposesNewRoutes()
    {
        var r = Request(); await using(var db = Db())
        {
            Assert.True((await db.Notifications.FirstAsync()).IsInboxVisible);
            db.Notifications.Add(new() { Id = Guid.NewGuid(),UserId = r.BuyerUserId!.Value,Title = "Legacy",Body = "Legacy",Status = NotificationStatus.Completed }); await db.SaveChangesAsync();
        }
        Assert.Single((await Inbox(r.BuyerUserId!.Value)).Items); var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route,swagger); Assert.Contains("{notificationId}",swagger);
    }

    private sealed class GateSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct = default)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); return result; }
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
