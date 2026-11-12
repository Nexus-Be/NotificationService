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
using Notification.Application.Services.OrderSettlements;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class OrderSettlementApiIntegrationTests : IAsyncLifetime
{
    private const string Route = "/api/v1/notifications/order-settlements";
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
    private OrderSettlementService Service(AppDbContext db) => new(db,_clock);
    private CreateOrderSettlementRequest Request(bool auction = false) => new()
    {
        SourceEventId=Guid.NewGuid(),SettlementId=Guid.NewGuid(),WalletCreditTransactionId=Guid.NewGuid(),OrderId=Guid.NewGuid(),SellerUserId=Guid.NewGuid(),OrderKind=auction ? "Auction" : "Regular",AuctionId=auction ? Guid.NewGuid() : null,OrderNo="ORDER-<b>DEMO</b>",NetAmount=125000.50m,Currency="VND",
        OrderStatus="Completed",SettlementStatus="Completed",WalletCreditStatus="Completed",SettlementDestination="SellerWallet",OrderCompletedAt=_clock.GetUtcNow().AddMinutes(-5),WalletCreditedAt=_clock.GetUtcNow().AddMinutes(-2),SettledAt=_clock.GetUtcNow().AddMinutes(-1)
    };
    private async Task<OrderSettlementResponse> Accept(CreateOrderSettlementRequest request)
    { await using var db = Db(); return await Service(db).CreateAsync(request); }
    private async Task<bool> Process()
    { await using var db = Db(); return await new DeliveryService(db,_sender,new(),_clock,_push).ProcessNextAsync(); }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_order_settlement_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(OrderSettlementsController).Assembly)
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
    public async Task BothKinds_OnlySellerReceivesExactNetCreditWithoutBankOrBalanceClaim()
    {
        foreach(var auction in new[] { false,true })
        {
            Role("service"); var r=Request(auction); await Device(r.SellerUserId!.Value,2); var other=Guid.NewGuid(); await Device(other); var response=await _client.PostAsJsonAsync(Route,r); await Check(response,HttpStatusCode.OK); Assert.True(response.Headers.CacheControl!.NoStore); var accepted=(await response.Content.ReadFromJsonAsync<OrderSettlementResponse>())!; Assert.Equal(r.SettlementId,accepted.SettlementId); Assert.Equal(r.SellerUserId,accepted.UserId); Assert.Equal(3,accepted.Deliveries.Count);
            Assert.True(await Process()); Assert.True(await Process()); var item=Assert.Single((await Inbox(r.SellerUserId.Value)).Items); Assert.Empty((await Inbox(other)).Items); Assert.Contains("125,000.50",item.Body); Assert.Contains("ví người bán",item.Body); Assert.DoesNotContain("ngân hàng",item.Body); Assert.DoesNotContain("có thể rút",item.Body); Assert.DoesNotContain("số dư là",item.Body); Assert.DoesNotContain("chiết khấu",item.Body);
            using var p=JsonDocument.Parse(item.Payload!); Assert.Equal("Seller",p.RootElement.GetProperty("audience").GetString()); Assert.Equal("OrderSellerSettled",p.RootElement.GetProperty("type").GetString()); Assert.Equal("SellerWallet",p.RootElement.GetProperty("settlementDestination").GetString()); Assert.Equal(r.NetAmount,p.RootElement.GetProperty("netAmount").GetDecimal()); Assert.Equal(r.WalletCreditTransactionId,p.RootElement.GetProperty("walletCreditTransactionId").GetGuid()); Assert.False(p.RootElement.TryGetProperty("balance",out _)); Assert.DoesNotContain(r.SellerUserId.ToString()!,item.Payload);
        }
        Assert.Equal(4,_push.Messages.Count); Assert.Empty(_sender.Messages); await using var db=Db(); Assert.Equal(2,await db.OrderSettlementDispatches.CountAsync()); Assert.Equal(2,await db.AuditLogs.CountAsync(a=>a.Action==AuditAction.OrderSettlementAccepted)); Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task UnconfirmedOrderSettlementCreditOrWrongDestination_AreRejected()
    {
        var r=Request(); foreach(var invalid in new[] { r with { OrderStatus="Delivered" },r with { OrderStatus="Disputed" },r with { SettlementStatus="Pending" },r with { SettlementStatus="Failed" },r with { SettlementStatus="Requested" },r with { WalletCreditStatus="Pending" },r with { WalletCreditStatus="Failed" },r with { SettlementDestination="Bank" },r with { SettlementDestination="BuyerWallet" } }) await Check(await _client.PostAsJsonAsync(Route,invalid),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT"); await using var db=Db(); Assert.Empty(await db.OrderSettlementDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task InvalidIdsKindMoneyAndTimeline_AreRejectedWithoutNotification()
    {
        var r=Request(); foreach(var invalid in new[] { r with { SourceEventId=Guid.Empty },r with { SettlementId=null },r with { WalletCreditTransactionId=Guid.Empty },r with { OrderId=Guid.Empty },r with { SellerUserId=null },r with { OrderKind="Other" },r with { AuctionId=Guid.NewGuid() },Request(true) with { AuctionId=null },r with { NetAmount=0 },r with { NetAmount=-1 },r with { NetAmount=0.001m },r with { NetAmount=10000000000000000m },r with { Currency="USD" },r with { OrderNo="bad\n" },r with { TraceId="\n" },r with { OrderCompletedAt=null },r with { WalletCreditedAt=null },r with { SettledAt=null },r with { WalletCreditedAt=r.OrderCompletedAt!.Value.AddSeconds(-1) },r with { SettledAt=r.WalletCreditedAt!.Value.AddSeconds(-1) },r with { SettledAt=_clock.GetUtcNow().AddMinutes(1) } }) await Check(await _client.PostAsJsonAsync(Route,invalid),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT"); await using var db=Db(); Assert.Empty(await db.OrderSettlementDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task UnknownBuyerFeeBankBalancePrivateFields_AreNotAccepted()
    {
        var r=Request(); foreach(var field in new[] { "buyerUserId","grossAmount","commissionRate","feeAmount","withdrawable","walletBalance","email","phone","bankAccount","cardNumber","paymentUrl","refundAmount","stockReleased","reputationPoints","password","otpCode","token" }) { var b=JsonSerializer.SerializeToNode(r)!.AsObject(); b[field]="private"; await Check(await _client.PostAsync(Route,new StringContent(b.ToJsonString(),Encoding.UTF8,"application/json")),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT"); } await using var db=Db(); Assert.Empty(await db.OrderSettlementDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task CanonicalReplay_PreservesExactAmountTimesAndOriginalFanout()
    {
        var r=Request(true); await Device(r.SellerUserId!.Value); var first=await Accept(r); await Device(r.SellerUserId.Value); await using(var db=Db()) await db.NotificationTemplates.Where(t=>t.Code=="ORDER_SELLER_SETTLED_PUSH").ExecuteUpdateAsync(s=>s.SetProperty(t=>t.IsActive,false));
        var replay=await Accept(r with { OrderKind="auction",OrderNo=" "+r.OrderNo+" ",NetAmount=125000.500m,Currency="vnd",OrderStatus="completed",SettlementStatus="completed",WalletCreditStatus="completed",SettlementDestination="sellerwallet",OrderCompletedAt=r.OrderCompletedAt!.Value.ToOffset(TimeSpan.FromHours(7)),WalletCreditedAt=r.WalletCreditedAt!.Value.ToOffset(TimeSpan.FromHours(7)),SettledAt=r.SettledAt!.Value.ToOffset(TimeSpan.FromHours(7)),TraceId="retry" }); Assert.True(replay.IsReplay); Assert.Equal(first.NotificationId,replay.NotificationId); Assert.Equal(first.Deliveries.Select(d=>d.Id),replay.Deliveries.Select(d=>d.Id));
    }

    [PostgresFact]
    public async Task ChangedSellerAmountOrSourceProof_ConflictsAndHiddenKeysStillDeduplicate()
    {
        var r=Request(true); var accepted=await Accept(r); foreach(var changed in new[] { r with { SellerUserId=Guid.NewGuid() },r with { SettlementId=Guid.NewGuid() },r with { WalletCreditTransactionId=Guid.NewGuid() },r with { OrderId=Guid.NewGuid() },r with { AuctionId=Guid.NewGuid() },r with { NetAmount=1 },r with { SettledAt=r.SettledAt!.Value.AddSeconds(1) } }) { var e=await Assert.ThrowsAsync<DomainException>(()=>Accept(changed)); Assert.Equal("ORDER_SETTLEMENT_CONFLICT",e.ErrorCode); }
        await using(var db=Db()) { await db.OrderSettlementDispatches.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true)); await db.Notifications.Where(n=>n.Id==accepted.NotificationId).ExecuteUpdateAsync(s=>s.SetProperty(n=>n.IsDeleted,true)); }
        foreach(var alias in new[] { Request() with { SettlementId=r.SettlementId },Request() with { WalletCreditTransactionId=r.WalletCreditTransactionId },Request(true) with { OrderId=r.OrderId },Request(true) with { AuctionId=r.AuctionId } }) { var e=await Assert.ThrowsAsync<DomainException>(()=>Accept(alias)); Assert.Equal("ORDER_SETTLEMENT_ALREADY_NOTIFIED",e.ErrorCode); } Assert.True((await Accept(r)).IsReplay);
    }

    [PostgresFact]
    public async Task OneFinalSettlementPerOrder_ButSeparateKindsAndFreshOrdersAreAllowed()
    {
        var r=Request(); await Accept(r); var e=await Assert.ThrowsAsync<DomainException>(()=>Accept(Request() with { OrderId=r.OrderId,SellerUserId=r.SellerUserId })); Assert.Equal("ORDER_SETTLEMENT_ALREADY_NOTIFIED",e.ErrorCode);
        await Accept(Request(true) with { OrderId=r.OrderId,SellerUserId=r.SellerUserId }); await Accept(Request() with { SellerUserId=r.SellerUserId }); Assert.Equal(3,(await Inbox(r.SellerUserId!.Value)).Items.Count);
    }

    [PostgresFact]
    public async Task ConcurrentSettlementCreditOrderOrAuctionAliases_AcceptOnlyOneOperation()
    {
        for(var key=0;key<4;key++)
        {
            var r=Request(key==3); var alias=Request(key==3); alias=key switch { 0=>alias with { SettlementId=r.SettlementId },1=>alias with { WalletCreditTransactionId=r.WalletCreditTransactionId },2=>alias with { OrderId=r.OrderId },_=>alias with { AuctionId=r.AuctionId } };
            var statuses=await Task.WhenAll(new[] { r,alias }.Select(async x=> { try { await Accept(x); return 200; } catch(DomainException e) { Assert.Equal("ORDER_SETTLEMENT_ALREADY_NOTIFIED",e.ErrorCode); return e.StatusCode; } })); Assert.Single(statuses,x=>x==200); Assert.Single(statuses,x=>x==409);
        }
    }

    [PostgresFact]
    public async Task MandatoryPushOptionalInboxAndGlobalDisable_AffectCorrectChannel()
    {
        var r=Request(); await Device(r.SellerUserId!.Value); await Preference(r.SellerUserId.Value,ChannelType.Push); await Preference(r.SellerUserId.Value,ChannelType.InApp); var result=await Accept(r); Assert.False(result.InboxVisible); Assert.Contains(result.Deliveries,d=>d.Channel=="Push" && d.Status=="Pending"); Assert.Contains(result.Deliveries,d=>d.Channel=="InApp" && d.ErrorCode=="PREFERENCE_DISABLED"); Assert.True(await Process()); Assert.Empty((await Inbox(r.SellerUserId.Value)).Items);
        await using(var db=Db()) await db.NotificationChannels.Where(c=>c.Code==ChannelType.Push).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.IsDisabled,true)); Assert.Contains((await Accept(Request())).Deliveries,d=>d.ErrorCode=="CHANNEL_DISABLED");
    }

    [PostgresFact]
    public async Task HistoricalAndExactCreditTimes_ArePreservedWithoutInventingSettlementSchedule()
    {
        var now=_clock.GetUtcNow(); await Accept(Request() with { OrderCompletedAt=now,WalletCreditedAt=now,SettledAt=now }); var r=Request(true) with { OrderCompletedAt=now.AddDays(-5),WalletCreditedAt=now.AddDays(-2),SettledAt=now.AddDays(-2) }; await Accept(r); var item=Assert.Single((await Inbox(r.SellerUserId!.Value)).Items); using var p=JsonDocument.Parse(item.Payload!); Assert.Equal(r.SettledAt!.Value.UtcTicks/10*10,p.RootElement.GetProperty("settledAt").GetDateTimeOffset().UtcTicks); Assert.Equal(r.OrderCompletedAt!.Value.UtcTicks/10*10,p.RootElement.GetProperty("orderCompletedAt").GetDateTimeOffset().UtcTicks);
    }

    [PostgresFact]
    public async Task TemplateConfigurationRenderAndMissingChannel_DoNotAcceptSettlement()
    {
        await using(var db=Db()) await db.NotificationTemplates.Where(t=>t.Code=="ORDER_SELLER_SETTLED_PUSH").ExecuteUpdateAsync(s=>s.SetProperty(t=>t.IsActive,false)); await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.NotFound,"TEMPLATE_NOT_FOUND");
        await using(var db=Db()) await db.NotificationTemplates.Where(t=>t.Code=="ORDER_SELLER_SETTLED_PUSH").ExecuteUpdateAsync(s=>s.SetProperty(t=>t.IsActive,true).SetProperty(t=>t.IsMandatory,false)); await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.Conflict,"ORDER_SETTLEMENT_TEMPLATE_INVALID");
        await using(var db=Db()) await db.NotificationTemplates.Where(t=>t.Code=="ORDER_SELLER_SETTLED_PUSH").ExecuteUpdateAsync(s=>s.SetProperty(t=>t.IsMandatory,true).SetProperty(t=>t.BodyTemplate,"{{WalletBalance}}")); await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.BadRequest,"TEMPLATE_DATA_MISSING");
        await using(var db=Db()) await db.NotificationTemplates.Where(t=>t.Code=="ORDER_SELLER_SETTLED_PUSH").ExecuteUpdateAsync(s=>s.SetProperty(t=>t.BodyTemplate,"{{OrderNo")); await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.Conflict,"TEMPLATE_INVALID");
        await using(var db=Db()) await db.NotificationChannels.Where(c=>c.Code==ChannelType.InApp).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.IsDeleted,true)); await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.Conflict,"CHANNEL_NOT_FOUND"); await using var verify=Db(); Assert.Empty(await verify.OrderSettlementDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task GenericCollisionAndBrokenManifest_NeverAdoptUnknownFinancialContent()
    {
        var r=Request(); await using(var db=Db()) await new DispatchService(db).CreateAsync(new() { SourceEventId=r.SourceEventId,UserId=r.SellerUserId,TemplateCode="ORDER_SELLER_SETTLED_PUSH",Data=new() { ["OrderNo"]="Other",["NetAmount"]="Other",["SettledAtUtc"]="Other" } }); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"ORDER_SETTLEMENT_CONFLICT");
        var other=Request(); var accepted=await Accept(other); await using(var db=Db()) { await db.NotificationLogs.Where(l=>l.NotificationId==accepted.NotificationId).ExecuteDeleteAsync(); await db.NotificationDeliveries.Where(d=>d.NotificationId==accepted.NotificationId).ExecuteDeleteAsync(); } await Check(await _client.PostAsJsonAsync(Route,other),HttpStatusCode.Conflict,"DELIVERY_STATE_INVALID");
    }

    [PostgresFact]
    public async Task EarlierOrderCompletion_RemainsSeparate_AndBuyerCannotReadSellerCredit()
    {
        foreach(var auction in new[] { false,true })
        {
            var r=Request(auction); var buyer=Guid.NewGuid(); await using(var db=Db()) await new Notification.Application.Services.OrderCompletions.OrderCompletionService(db,_clock).CreateAsync(new() { SourceEventId=Guid.NewGuid(),OrderId=r.OrderId,ShopId=Guid.NewGuid(),ShipmentId=Guid.NewGuid(),BuyerUserId=buyer,SellerUserId=r.SellerUserId,OrderKind=r.OrderKind,AuctionId=r.AuctionId,OrderNo=r.OrderNo,OrderStatus="Completed",DeliveryStatus="Delivered",PaidAt=r.OrderCompletedAt!.Value.AddDays(-3),DeliveredAt=r.OrderCompletedAt.Value.AddHours(-1),CompletedAt=r.OrderCompletedAt });
            var result=await Accept(r); Assert.Equal(2,(await Inbox(r.SellerUserId!.Value)).Items.Count); var buyerItem=Assert.Single((await Inbox(buyer)).Items); Assert.DoesNotContain("125,000.50",buyerItem.Body); Role("buyer",buyer); await Check(await _client.GetAsync(Route+"/"+result.NotificationId),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
        }
        await using var verify=Db(); Assert.Equal(2,await verify.OrderCompletionDispatches.CountAsync()); Assert.Equal(2,await verify.OrderSettlementDispatches.CountAsync());
    }

    [PostgresFact]
    public async Task SwaggerPreferenceMatrixAndEnums_ExposeSellerSettlementWithoutChangingPriorTypes()
    {
        var swagger=await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route,swagger); Assert.Contains("netAmount",swagger); Assert.Contains("settlementDestination",swagger); Role("seller"); var prefs=(await _client.GetFromJsonAsync<List<NotificationPreferenceResponse>>("/api/v1/notifications/preferences"))!; Assert.Equal(54,prefs.Count); Assert.Equal(18,prefs.Count(p=>p.IsMandatory)); Assert.True(prefs.Single(p=>p.NotificationType=="OrderSellerSettled" && p.ChannelCode=="Push").IsMandatory); Assert.False(prefs.Single(p=>p.NotificationType=="OrderSellerSettled" && p.ChannelCode=="InApp").IsMandatory); Assert.Equal(15,(int)NotificationType.OrderSellerSettled); Assert.Equal(14,(int)NotificationType.OrderCompleted); Assert.Equal(27,(int)AuditAction.OrderSettlementAccepted);
    }

    [PostgresFact]
    public async Task ConcurrentSameEvent_AcceptsOnce_ChangedRecipientConflicts()
    {
        var r = Request(); var replies = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Accept(r))); Assert.Single(replies,x => !x.IsReplay); Assert.Single(replies.Select(x => x.NotificationId).Distinct());
        var other = Request(); var statuses = await Task.WhenAll(new[] { other,other with { SellerUserId = Guid.NewGuid() } }.Select(async x => { try { await Accept(x); return 200; } catch(DomainException ex) { Assert.Equal("ORDER_SETTLEMENT_CONFLICT",ex.ErrorCode); return ex.StatusCode; } })); Assert.Single(statuses,x => x == 200); Assert.Single(statuses,x => x == 409);
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
        Assert.Empty((await Inbox(r.SellerUserId.Value,true)).Items); Role("seller",Guid.NewGuid()); await Check(await _client.GetAsync(path),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
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
        await using(var db = Db()) { Assert.Empty(await db.OrderSettlementDispatches.ToListAsync()); Assert.Empty(await db.Notifications.Where(n => n.SourceEventId == r.SourceEventId).ToListAsync()); Assert.Empty(await db.AuditLogs.Where(a => a.Action == AuditAction.OrderSettlementAccepted).ToListAsync()); }
        Assert.False((await Accept(r)).IsReplay);
    }

    [PostgresFact]
    public async Task TemplateSharedLock_PreventsMetadataEditBeforeAcceptanceCommit()
    {
        var r = Request(); var gate = new GateSave(); await using var acceptingDb = Db(gate); var accepting = Service(acceptingDb).CreateAsync(r); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var editor = Db(); await using var tx = await editor.Database.BeginTransactionAsync(); await editor.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => editor.NotificationTemplates.Where(t => t.Code == "ORDER_SELLER_SETTLED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.NotificationType,NotificationType.Promotion))); Assert.Equal("55P03",Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        finally { gate.Release.TrySetResult(); }
        Assert.False((await accepting.WaitAsync(TimeSpan.FromSeconds(5))).IsReplay);
    }

    [PostgresFact]
    public async Task RoleAndBodyLimit_OnlyServiceOrAdminCanAccept()
    {
        var r = Request(); Role(null); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Unauthorized,"UNAUTHORIZED");
        foreach(var role in new[] { "buyer","seller","staff" }) { Role(role); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Forbidden,"FORBIDDEN"); }
        Role("service"); await Check(await _client.PostAsJsonAsync(Route,r with { OrderNo = new string('x',17000) }),HttpStatusCode.RequestEntityTooLarge,"REQUEST_BODY_TOO_LARGE"); Role("admin"); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task WorkerFailureRetry_KeepsInboxAndOriginalPlainTextSnapshot()
    {
        var r = Request(); await Device(r.SellerUserId!.Value); _push.Result = _ => PushDeliveryResult.Failure("FCM_UNAVAILABLE","Test transient failure.",true); await Accept(r); Assert.True(await Process()); var original = Assert.Single(_push.Messages).Body;
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "ORDER_SELLER_SETTLED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"Changed later"));
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
        db.NotificationPreferences.Add(new() { Id = Guid.NewGuid(),UserId = owner,NotificationType = NotificationType.OrderSellerSettled,ChannelCode = channel,IsDisabled = disabled });
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
