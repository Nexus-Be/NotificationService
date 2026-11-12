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
using Notification.Application.Services.OrderDecisions;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class OrderDecisionApiIntegrationTests : IAsyncLifetime
{
    private const string Route = "/api/v1/notifications/order-decisions";
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
    private OrderDecisionService Service(AppDbContext db) => new(db,_clock);
    private CreateOrderDecisionRequest Request(string outcome = "Accepted",bool auction = false) => new()
    {
        SourceEventId=Guid.NewGuid(),OrderId=Guid.NewGuid(),ShopId=Guid.NewGuid(),BuyerUserId=Guid.NewGuid(),SellerUserId=Guid.NewGuid(),OrderNo="ORDER-<b>DEMO</b>",
        OrderKind=auction ? "Auction" : "Regular",AuctionId=auction ? Guid.NewGuid() : null,Outcome=outcome,OrderStatus=outcome=="Accepted" ? "Shipping" : "Cancelled",Reason=outcome=="Accepted" ? null : "Source user-safe reason",
        PaidAt=_clock.GetUtcNow().AddHours(-26),SellerAcceptStartedAt=_clock.GetUtcNow().AddHours(-25),SellerAcceptDeadline=_clock.GetUtcNow().AddHours(outcome=="AcceptanceTimedOut" ? -1 : 1),DecidedAt=_clock.GetUtcNow().AddMinutes(-1)
    };
    private async Task<OrderDecisionResponse> Accept(CreateOrderDecisionRequest request)
    { await using var db = Db(); return await Service(db).CreateAsync(request); }
    private async Task<bool> Process()
    { await using var db = Db(); return await new DeliveryService(db,_sender,new(),_clock,_push).ProcessNextAsync(); }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_order_decision_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(OrderDecisionsController).Assembly)
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
    public async Task SixKindOutcomeCombinations_CreateAtomicPrivateBuyerSellerItems()
    {
        foreach(var auction in new[] { false,true }) foreach(var outcome in new[] { "Accepted","Rejected","AcceptanceTimedOut" })
        {
            Role("service"); var r=Request(outcome,auction); await Device(r.BuyerUserId!.Value); await Device(r.SellerUserId!.Value);
            var response=await _client.PostAsJsonAsync(Route,r); await Check(response,HttpStatusCode.OK); Assert.True(response.Headers.CacheControl!.NoStore); var accepted=(await response.Content.ReadFromJsonAsync<OrderDecisionResponse>())!;
            Assert.True(await Process()); Assert.True(await Process()); var buyer=Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); var seller=Assert.Single((await Inbox(r.SellerUserId.Value)).Items);
            Assert.DoesNotContain(r.SellerUserId.ToString()!,buyer.Payload); Assert.DoesNotContain(r.BuyerUserId.ToString()!,seller.Payload); Assert.DoesNotContain("đã hoàn tiền",buyer.Body); Assert.DoesNotContain("đã trừ",seller.Body); Assert.DoesNotContain("đã nhả kho",buyer.Body); Assert.DoesNotContain("đã giao",buyer.Body); Assert.Contains(r.OrderNo,buyer.Body);
            using var payload=JsonDocument.Parse(buyer.Payload!); Assert.Equal(r.OrderKind,payload.RootElement.GetProperty("orderKind").GetString()); Assert.Equal(outcome,payload.RootElement.GetProperty("outcome").GetString()); Assert.Equal(r.OrderStatus,payload.RootElement.GetProperty("orderStatus").GetString()); Assert.Equal(2,accepted.Buyer.Deliveries.Count);
        }
        Assert.Equal(12,_push.Messages.Count); Assert.Empty(_sender.Messages); await using var db=Db(); Assert.Equal(6,await db.OrderDecisionDispatches.CountAsync()); Assert.Equal(6,await db.AuditLogs.CountAsync(a => a.Action==AuditAction.OrderDecisionAccepted)); Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task ConditionalFieldsConfirmedStatesAndClock_RejectInvalidCombinations()
    {
        var r=Request(); var timeout=Request("AcceptanceTimedOut"); var rejected=Request("Rejected");
        foreach(var invalid in new[] { r with { SourceEventId=Guid.Empty },r with { OrderId=null },r with { ShopId=Guid.Empty },r with { BuyerUserId=r.SellerUserId },r with { OrderKind="Other" },r with { AuctionId=Guid.NewGuid() },Request(auction:true) with { AuctionId=null },r with { Outcome="Pending" },r with { OrderStatus="Paid" },rejected with { OrderStatus="Shipping" },r with { Reason="Unexpected" },rejected with { Reason=" " },r with { OrderNo="bad\n" },rejected with { Reason="bad\n" },r with { TraceId="\n" },r with { PaidAt=null },r with { SellerAcceptStartedAt=r.PaidAt!.Value.AddSeconds(-1) },r with { SellerAcceptDeadline=r.SellerAcceptStartedAt },r with { DecidedAt=r.SellerAcceptDeadline!.Value.AddSeconds(1) },r with { DecidedAt=r.SellerAcceptStartedAt!.Value.AddSeconds(-1) },r with { DecidedAt=_clock.GetUtcNow().AddMinutes(1) },timeout with { DecidedAt=timeout.SellerAcceptDeadline!.Value.AddSeconds(-1) },timeout with { SellerAcceptDeadline=_clock.GetUtcNow().AddSeconds(1),DecidedAt=_clock.GetUtcNow().AddSeconds(1) } })
            await Check(await _client.PostAsJsonAsync(Route,invalid),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await using var db=Db(); Assert.Empty(await db.OrderDecisionDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task AuthUnknownFinancialAndPrivateFlagsAndBodyLimit_AreEnforced()
    {
        var r=Request(); Role(null); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Unauthorized);
        foreach(var role in new[] { "buyer","seller","staff" }) { Role(role); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Forbidden); }
        Role("service"); foreach(var field in new[] { "totalAmount","refundStatus","refunded","stockReleased","reputationPoints","shipmentId","email","phone","bankAccount","paymentUrl","password","otpCode","token" })
        { var body=JsonSerializer.SerializeToNode(r)!.AsObject(); body[field]="private"; await Check(await _client.PostAsync(Route,new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json")),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT"); }
        await Check(await _client.PostAsJsonAsync(Route,r with { OrderNo=new string('x',17000) }),HttpStatusCode.RequestEntityTooLarge,"REQUEST_BODY_TOO_LARGE"); Role("admin"); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task CanonicalReplayPreservesTimeReasonSuppressionAndOriginalFanout()
    {
        var r=Request("Rejected",true); await Device(r.BuyerUserId!.Value); var first=await Accept(r); await Device(r.BuyerUserId.Value);
        await using(var db=Db()) await db.NotificationTemplates.Where(t => t.Code=="ORDER_REJECTED_BUYER_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,false));
        var replay=await Accept(r with { OrderKind="auction",Outcome="rejected",OrderStatus="cancelled",OrderNo=" "+r.OrderNo+" ",Reason=" "+r.Reason+" ",PaidAt=r.PaidAt!.Value.ToOffset(TimeSpan.FromHours(7)),SellerAcceptStartedAt=r.SellerAcceptStartedAt!.Value.ToOffset(TimeSpan.FromHours(7)),SellerAcceptDeadline=r.SellerAcceptDeadline!.Value.ToOffset(TimeSpan.FromHours(7)),DecidedAt=r.DecidedAt!.Value.ToOffset(TimeSpan.FromHours(7)),TraceId="retry" });
        Assert.True(replay.IsReplay); Assert.Equal(first.Buyer.NotificationId,replay.Buyer.NotificationId); Assert.Equal(first.Buyer.Deliveries.Select(d=>d.Id),replay.Buyer.Deliveries.Select(d=>d.Id));
    }

    [PostgresFact]
    public async Task ChangedSnapshotAndOutcome_ConflictAcrossHiddenOrderAndAuctionKeys()
    {
        var r=Request("Rejected",true); await Accept(r);
        foreach(var changed in new[] { r with { SellerUserId=Guid.NewGuid() },r with { BuyerUserId=Guid.NewGuid() },r with { AuctionId=Guid.NewGuid() },r with { Reason="changed" },r with { SellerAcceptDeadline=r.SellerAcceptDeadline!.Value.AddMinutes(1) },r with { Outcome="Accepted",OrderStatus="Shipping",Reason=null } })
        { var e=await Assert.ThrowsAsync<DomainException>(() => Accept(changed)); Assert.Equal("ORDER_DECISION_CONFLICT",e.ErrorCode); }
        await using(var db=Db()) await db.OrderDecisionDispatches.ExecuteUpdateAsync(s => s.SetProperty(x=>x.IsDeleted,true));
        foreach(var alias in new[] { Request("Rejected",true) with { OrderId=r.OrderId },Request("Accepted",true) with { AuctionId=r.AuctionId } })
        { var e=await Assert.ThrowsAsync<DomainException>(() => Accept(alias)); Assert.Equal("ORDER_DECISION_ALREADY_NOTIFIED",e.ErrorCode); }
        Assert.True((await Accept(r)).IsReplay);
    }

    [PostgresFact]
    public async Task SeparateOrderKindsAndFreshOrders_AllowSameGuidWithoutMixingDomains()
    {
        var regular=Request(); await Accept(regular); var auction=Request(auction:true) with { OrderId=regular.OrderId,BuyerUserId=regular.BuyerUserId,SellerUserId=regular.SellerUserId }; await Accept(auction);
        Assert.Equal(2,(await Inbox(regular.BuyerUserId!.Value)).Items.Count); await Accept(Request() with { BuyerUserId=regular.BuyerUserId,SellerUserId=regular.SellerUserId }); await using var db=Db(); Assert.Equal(3,await db.OrderDecisionDispatches.CountAsync());
    }

    [PostgresFact]
    public async Task ConcurrentAlternativeOutcomes_AcceptOnlyOneFinalSellerDecision()
    {
        var a=Request(); var b=a with { SourceEventId=Guid.NewGuid(),Outcome="Rejected",OrderStatus="Cancelled",Reason="Source reason" };
        var results=await Task.WhenAll(new[] { a,b }.Select(async r=> { try { await Accept(r); return 200; } catch(DomainException e) { Assert.Equal("ORDER_DECISION_ALREADY_NOTIFIED",e.ErrorCode); return e.StatusCode; } })); Assert.Single(results,x=>x==200); Assert.Single(results,x=>x==409);
    }

    [PostgresFact]
    public async Task MandatoryPushAndOptionalInApp_RespectOwnChannelAndGlobalDisable()
    {
        var r=Request(); await Device(r.BuyerUserId!.Value); await using(var db=Db()) { foreach(var c in new[] { ChannelType.Push,ChannelType.InApp }) db.NotificationPreferences.Add(new() { Id=Guid.NewGuid(),UserId=r.BuyerUserId.Value,NotificationType=NotificationType.OrderDecision,ChannelCode=c,IsDisabled=true }); await db.SaveChangesAsync(); }
        var result=await Accept(r); Assert.False(result.Buyer.InboxVisible); Assert.Contains(result.Buyer.Deliveries,d=>d.Channel=="Push" && d.Status=="Pending"); Assert.Contains(result.Buyer.Deliveries,d=>d.Channel=="InApp" && d.ErrorCode=="PREFERENCE_DISABLED"); Assert.True(await Process()); Assert.Empty((await Inbox(r.BuyerUserId.Value)).Items);
        await using(var db=Db()) await db.NotificationChannels.Where(c=>c.Code==ChannelType.Push).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.IsDisabled,true)); Assert.Contains((await Accept(Request())).Buyer.Deliveries,d=>d.ErrorCode=="CHANNEL_DISABLED");
    }

    [PostgresFact]
    public async Task ExactBoundaryAndHistoricalDeadline_KeepAuthoritativeSourceTimes()
    {
        var r=Request("AcceptanceTimedOut") with { SellerAcceptDeadline=_clock.GetUtcNow(),DecidedAt=_clock.GetUtcNow() }; await Accept(r);
        var late=Request("Rejected") with { PaidAt=_clock.GetUtcNow().AddDays(-5),SellerAcceptStartedAt=_clock.GetUtcNow().AddDays(-4),SellerAcceptDeadline=_clock.GetUtcNow().AddDays(-3),DecidedAt=_clock.GetUtcNow().AddDays(-3) }; await Accept(late);
        var item=Assert.Single((await Inbox(late.BuyerUserId!.Value)).Items); using var p=JsonDocument.Parse(item.Payload!); Assert.Equal(late.SellerAcceptDeadline!.Value.UtcTicks/10*10,p.RootElement.GetProperty("sellerAcceptDeadline").GetDateTimeOffset().UtcTicks);
    }

    [PostgresFact]
    public async Task RetryAfterTemplateEdit_UsesOriginalCancellationTextWithoutRefundClaim()
    {
        var r=Request("Rejected"); await Device(r.BuyerUserId!.Value); await Accept(r); _push.Result=_=>PushDeliveryResult.Failure("FCM_UNAVAILABLE","Test transient failure.",true); Assert.True(await Process()); var original=Assert.Single(_push.Messages).Body;
        await using(var db=Db()) await db.NotificationTemplates.Where(t=>t.Code=="ORDER_REJECTED_BUYER_PUSH").ExecuteUpdateAsync(s=>s.SetProperty(t=>t.BodyTemplate,"Changed later"));
        _clock.Advance(TimeSpan.FromSeconds(10)); _push.Result=_=>PushDeliveryResult.Sent("ok"); Assert.True(await Process()); Assert.Equal(original,_push.Messages.Last().Body); Assert.DoesNotContain("đã hoàn tiền",original); Assert.Single((await Inbox(r.BuyerUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task BrokenOneBranchManifest_DoesNotCreateReplacementPairOnReplay()
    {
        var r=Request(); var result=await Accept(r); await using(var db=Db()) { await db.NotificationLogs.Where(l=>l.NotificationId==result.Seller.NotificationId).ExecuteDeleteAsync(); await db.NotificationDeliveries.Where(d=>d.NotificationId==result.Seller.NotificationId).ExecuteDeleteAsync(); }
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"DELIVERY_STATE_INVALID"); await using var verify=Db(); Assert.Equal(2,await verify.Notifications.CountAsync(n=>n.SourceEventId==r.SourceEventId));
    }

    [PostgresFact]
    public async Task PreferencesSwaggerAndEnums_ExposeNewTypeWithoutChangingPriorValues()
    {
        Role("buyer"); var prefs=(await _client.GetFromJsonAsync<List<NotificationPreferenceResponse>>("/api/v1/notifications/preferences"))!; Assert.Equal(54,prefs.Count); Assert.Equal(18,prefs.Count(p=>p.IsMandatory)); Assert.True(prefs.Single(p=>p.NotificationType=="OrderDecision" && p.ChannelCode=="Push").IsMandatory); Assert.False(prefs.Single(p=>p.NotificationType=="OrderDecision" && p.ChannelCode=="InApp").IsMandatory);
        var swagger=await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route,swagger); Assert.Contains("decidedAt",swagger); Assert.Equal(12,(int)NotificationType.OrderDecision); Assert.Equal(23,(int)AuditAction.OrderDecisionAccepted); Assert.Equal(1,(int)NotificationOrderKind.Auction);
    }

    [PostgresFact]
    public async Task EarlierPaymentNotices_AreRetainedForRegularAndAuctionOrders()
    {
        foreach(var auction in new[] { false,true })
        {
            var r=Request(auction:auction);
            await using(var db=Db())
            {
                if (auction) await new Notification.Application.Services.AuctionOrderPayments.AuctionOrderPaymentService(db,_clock).CreateAsync(new() { SourceEventId=Guid.NewGuid(),AuctionId=r.AuctionId,ResultId=Guid.NewGuid(),WinningBidId=Guid.NewGuid(),OrderId=r.OrderId,ShopId=r.ShopId,BuyerUserId=r.BuyerUserId,SellerUserId=r.SellerUserId,OrderNo=r.OrderNo,TotalAmount=100,Currency="VND",OrderStatus="Paid",AuctionStatus="Settled",PaidAt=r.PaidAt,SellerAcceptStartedAt=r.SellerAcceptStartedAt,SellerAcceptDeadline=r.SellerAcceptDeadline });
                else await new Notification.Application.Services.OrderPayments.OrderPaymentService(db,_clock).CreateAsync(new() { SourceEventId=Guid.NewGuid(),OrderId=r.OrderId,ShopId=r.ShopId,BuyerUserId=r.BuyerUserId,SellerUserId=r.SellerUserId,OrderNo=r.OrderNo,TotalAmount=100,Currency="VND",PaidAt=r.PaidAt,SellerAcceptDeadline=r.SellerAcceptDeadline });
            }
            await Accept(r); Assert.Equal(2,(await Inbox(r.BuyerUserId!.Value)).Items.Count); Assert.Equal(2,(await Inbox(r.SellerUserId!.Value)).Items.Count);
        }
        await using var verify=Db(); Assert.Single(await verify.OrderPaidDispatches.ToListAsync()); Assert.Single(await verify.AuctionOrderPaidDispatches.ToListAsync()); Assert.Equal(2,await verify.OrderDecisionDispatches.CountAsync());
    }

    [PostgresFact]
    public async Task NoDevices_StillCreatesTwoUnreadInboxItemsAndSkippedPushMarkers()
    {
        var r = Request(); var result = await Accept(r);
        Assert.Equal("Completed",result.Buyer.NotificationStatus); Assert.True(result.Buyer.InboxVisible);
        Assert.Contains(result.Buyer.Deliveries,d => d.Channel == "Push" && d.ErrorCode == "PUSH_NO_ACTIVE_DEVICES");
        Assert.Equal(1,(await Inbox(r.BuyerUserId!.Value,true)).Pagination.TotalCount); Assert.Equal(1,(await Inbox(r.SellerUserId!.Value,true)).Pagination.TotalCount);
        Assert.False(await Process());
    }

    [PostgresFact]
    public async Task PushFanout_UsesSameLogicalId_AndDoesNotMultiplyBellItems()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value,2); await Device(r.SellerUserId!.Value,3); await Device(Guid.NewGuid());
        var accepted = await Accept(r); for(var i = 0;i < 5;i++) Assert.True(await Process()); Assert.False(await Process());
        Assert.Equal(5,_push.Messages.Count); Assert.All(_push.Messages,m => Assert.True(m.NotificationId == accepted.Buyer.NotificationId || m.NotificationId == accepted.Seller.NotificationId));
        Assert.Equal(2,_push.Messages.Count(m => m.UserId == r.BuyerUserId)); Assert.Equal(3,_push.Messages.Count(m => m.UserId == r.SellerUserId));
        Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); Assert.Single((await Inbox(r.SellerUserId.Value)).Items);
        var replay = await Accept(r); Assert.Equal("Completed",replay.Buyer.NotificationStatus); Assert.Equal("Completed",replay.Seller.NotificationStatus);
    }

    [PostgresFact]
    public async Task BuyerPushFailure_DoesNotLoseEitherInboxOrSellerPush()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await Device(r.SellerUserId!.Value);
        _push.Result = m => m.UserId == r.BuyerUserId ? PushDeliveryResult.Failure("FCM_AUTH_FAILED","Test permanent failure.") : PushDeliveryResult.Sent("seller-ok");
        await Accept(r); await Process(); await Process(); var replay = await Accept(r);
        Assert.Equal("Partial",replay.Buyer.NotificationStatus); Assert.Equal("Completed",replay.Seller.NotificationStatus);
        Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); Assert.Single((await Inbox(r.SellerUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task DisabledPush_PreservesInboxAndNeverRequeuesOnReplayAfterEnable()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var result = await Accept(r); Assert.Contains(result.Buyer.Deliveries,d => d.Channel == "Push" && d.Status == "Skipped");
        Assert.Single((await Inbox(r.BuyerUserId.Value)).Items);
        await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,false));
        Assert.True((await Accept(r)).IsReplay); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task DisabledInApp_PushStillRuns_AndBellReadActionsCannotResurrectItem()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await Device(r.SellerUserId!.Value);
        await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var accepted = await Accept(r); Assert.False(accepted.Buyer.InboxVisible); Assert.Empty((await Inbox(r.BuyerUserId.Value)).Items);
        await Check(await _client.GetAsync(Route + "/" + accepted.Buyer.NotificationId),HttpStatusCode.OK);
        await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + accepted.Buyer.NotificationId + "/read-states",new { isRead = false }),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
        await Process(); await Process(); Assert.Equal(2,_push.Messages.Count); Assert.Empty((await Inbox(r.BuyerUserId.Value,true)).Items);
        await using var dbCheck = Db(); var row = await dbCheck.Notifications.SingleAsync(n => n.Id == accepted.Buyer.NotificationId);
        Assert.False(row.IsInboxVisible); Assert.False(row.IsDeleted); Assert.True(row.IsRead);
    }

    [PostgresFact]
    public async Task BothChannelsDisabled_RecordsSuppressionWithoutVisibleInboxOrProviderCall()
    {
        var r = Request(); await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push || c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var accepted = await Accept(r); Assert.False(accepted.Buyer.InboxVisible); Assert.All(accepted.Buyer.Deliveries,d => Assert.Equal("Skipped",d.Status));
        Assert.Empty((await Inbox(r.BuyerUserId!.Value)).Items); Assert.False(await Process());
        await Check(await _client.GetAsync(Route + "/" + accepted.Buyer.NotificationId),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
    }

    [PostgresFact]
    public async Task DeviceOverflow_SkipsOnlyAffectedPushBranch_AndKeepsBothInboxes()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value,101); await Device(r.SellerUserId!.Value);
        var accepted = await Accept(r); Assert.Contains(accepted.Buyer.Deliveries,d => d.ErrorCode == "PUSH_DEVICE_LIMIT");
        Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); Assert.Single((await Inbox(r.SellerUserId.Value)).Items);
        Assert.True(await Process()); Assert.False(await Process()); Assert.Equal(r.SellerUserId,Assert.Single(_push.Messages).UserId);
    }

    [PostgresFact]
    public async Task OwnerGetAndReadStates_ProtectRolesBadgeAndIndependentBranches()
    {
        var r = Request(); var result = await Accept(r); Role(null);
        await Check(await _client.GetAsync(Route + "/" + result.Buyer.NotificationId),HttpStatusCode.Unauthorized);
        Role("buyer",r.BuyerUserId); await Check(await _client.GetAsync(Route + "/" + result.Seller.NotificationId),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
        var detail = await _client.GetAsync(Route + "/" + result.Buyer.NotificationId); await Check(detail,HttpStatusCode.OK); Assert.True(detail.Headers.CacheControl!.NoStore);
        await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + result.Buyer.NotificationId + "/read-states",new { isRead = true }),HttpStatusCode.OK);
        Assert.Empty((await Inbox(r.BuyerUserId!.Value,true)).Items); Assert.Single((await Inbox(r.SellerUserId!.Value,true)).Items);
        Role("buyer",r.BuyerUserId); await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + result.Buyer.NotificationId + "/read-states",new { isRead = false }),HttpStatusCode.OK);
        Assert.Single((await Inbox(r.BuyerUserId.Value,true)).Items);
        await using var db = Db(); Assert.All(await db.NotificationDeliveries.Where(d => d.NotificationId == result.Buyer.NotificationId && d.ChannelCode == ChannelType.InApp).ToListAsync(),d => Assert.Equal(DeliveryStatus.Sent,d.Status));
    }

    [PostgresFact]
    public async Task ConcurrentSameEvent_CreatesTwoItemsOnceAndReplaysSevenTimes()
    {
        var r = Request(); var results = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Accept(r))).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results,x => !x.IsReplay); Assert.Single(results.Select(x => x.Buyer.NotificationId).Distinct());
        await using var db = Db(); Assert.Equal(2,await db.Notifications.CountAsync(n => n.SourceEventId == r.SourceEventId)); Assert.Single(await db.OrderDecisionDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentOrderAliases_CreateOnePairAndConflictOtherEvent()
    {
        var r = Request(); var statuses = await Task.WhenAll(new[] { r,r with { SourceEventId = Guid.NewGuid() } }.Select(async x =>
        { try { await Accept(x); return 200; } catch(DomainException ex) { Assert.Equal("ORDER_DECISION_ALREADY_NOTIFIED",ex.ErrorCode); return ex.StatusCode; } }));
        Assert.Single(statuses,x => x == 200); Assert.Single(statuses,x => x == 409);
        await using var db = Db(); Assert.Single(await db.OrderDecisionDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task SameEventDifferentOrdersRace_OnePairWinsOtherConflicts()
    {
        var r = Request(); var statuses = await Task.WhenAll(new[] { r,r with { OrderId = Guid.NewGuid(),SellerUserId = Guid.NewGuid() } }.Select(async x =>
        { try { await Accept(x); return 200; } catch(DomainException ex) { Assert.Equal("ORDER_DECISION_CONFLICT",ex.ErrorCode); return ex.StatusCode; } }));
        Assert.Single(statuses,x => x == 200); Assert.Single(statuses,x => x == 409);
    }

    [PostgresFact]
    public async Task AuditWriteFailure_RollsBackBothBranchesAllDeliveriesAndReceipt()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value);
        await using(var db = Db(new FailAllSave())) await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(r));
        await using var verify = Db(); Assert.Empty(await verify.OrderDecisionDispatches.ToListAsync()); Assert.False(await verify.Notifications.AnyAsync(n => n.SourceEventId == r.SourceEventId));
        Assert.Empty(await verify.AuditLogs.Where(a => a.Action == AuditAction.OrderDecisionAccepted).ToListAsync()); Assert.False(await Process()); Assert.False((await Accept(r)).IsReplay);
    }

    [PostgresFact]
    public async Task SellerTemplateFailure_DoesNotCommitBuyerBranch()
    {
        var r = Request(); await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "ORDER_ACCEPTED_SELLER_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{TotalAmount}}"));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.BadRequest,"TEMPLATE_DATA_MISSING");
        await using var verify = Db(); Assert.False(await verify.Notifications.AnyAsync(n => n.SourceEventId == r.SourceEventId)); Assert.Empty(await verify.OrderDecisionDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task InvalidTemplatesAndMissingChannels_AcceptNeitherBranch()
    {
        var r = Request(); await using var db = Db();
        await db.NotificationTemplates.Where(t => t.Code == "ORDER_ACCEPTED_BUYER_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory,false));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"ORDER_DECISION_TEMPLATE_INVALID");
        await db.NotificationTemplates.Where(t => t.Code == "ORDER_ACCEPTED_BUYER_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory,true).SetProperty(t => t.IsActive,false));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.NotFound,"TEMPLATE_NOT_FOUND");
        await db.NotificationTemplates.Where(t => t.Code == "ORDER_ACCEPTED_BUYER_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,true));
        await db.NotificationChannels.Where(c => c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted,true));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"CHANNEL_NOT_FOUND"); Assert.Empty(await db.OrderDecisionDispatches.ToListAsync());
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
    public async Task LegacyRowsRemainVisibleAndSwaggerExposesNewRoutes()
    {
        var r = Request(); await using(var db = Db())
        {
            Assert.True((await db.Notifications.FirstAsync()).IsInboxVisible);
            db.Notifications.Add(new() { Id = Guid.NewGuid(),UserId = r.BuyerUserId!.Value,Title = "Legacy",Body = "Legacy",Status = NotificationStatus.Completed }); await db.SaveChangesAsync();
        }
        Assert.Single((await Inbox(r.BuyerUserId!.Value)).Items); var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route,swagger); Assert.Contains("{notificationId}",swagger);
    }

    [PostgresFact]
    public async Task GenericDispatchCollision_RollsBackWholeNewPairInsteadOfAdoptingExistingContent()
    {
        var r = Request(); await using(var db = Db()) await new DispatchService(db).CreateAsync(new()
        {
            SourceEventId = r.SourceEventId,UserId = r.BuyerUserId,TemplateCode = "ORDER_ACCEPTED_BUYER_PUSH",
            Data = new() { ["OrderNo"] = "Different",["DecidedAtUtc"] = "Different" }
        });
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"ORDER_DECISION_CONFLICT");
        await using var verify = Db(); Assert.Empty(await verify.OrderDecisionDispatches.ToListAsync());
        Assert.False(await verify.Notifications.AnyAsync(n => n.SourceEventId == r.SourceEventId && n.UserId == r.SellerUserId));
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
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => editor.NotificationTemplates.Where(t => t.Code == "ORDER_ACCEPTED_SELLER_PUSH")
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.NotificationType,NotificationType.Promotion)));
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
