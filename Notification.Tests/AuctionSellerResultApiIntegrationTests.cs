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
using Notification.Application.Services.AuctionSellerResults;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class AuctionSellerResultApiIntegrationTests : IAsyncLifetime
{
    private const string Route = "/api/v1/notifications/auction-seller-results";
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
    private AuctionSellerResultService Service(AppDbContext db) => new(db,_clock);
    private CreateAuctionSellerResultRequest Request(bool noBids = false) => new()
    {
        SourceEventId = Guid.NewGuid(),AuctionId = Guid.NewGuid(),ResultId = Guid.NewGuid(),SellerUserId = Guid.NewGuid(),AuctionTitle = "<b>Product</b>",
        Outcome = noBids ? "NoBids" : "WinnerSelected",DecidedAt = _clock.GetUtcNow().AddMinutes(-1),
        WinningBidId = noBids ? null : Guid.NewGuid(),WinnerUserId = noBids ? null : Guid.NewGuid(),OrderId = noBids ? null : Guid.NewGuid(),
        WinningAmount = noBids ? null : 150000.50m,Currency = noBids ? null : "VND",PaymentDeadline = noBids ? null : _clock.GetUtcNow().AddHours(24)
    };
    private async Task<AuctionSellerResultResponse> Accept(CreateAuctionSellerResultRequest request)
    { await using var db = Db(); return await Service(db).CreateAsync(request); }
    private async Task<bool> Process()
    { await using var db = Db(); return await new DeliveryService(db,_sender,new(),_clock,_push).ProcessNextAsync(); }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_auction_seller_result_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(AuctionSellerResultsController).Assembly)
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
    public async Task WinnerSelected_NotifiesOnlySeller_OneGroupedItemAndPrivateHistoricalPayload()
    {
        var r = Request(); await Device(r.SellerUserId!.Value,2); await Device(r.WinnerUserId!.Value);
        var response = await _client.PostAsJsonAsync(Route,r); await Check(response,HttpStatusCode.OK); Assert.True(response.Headers.CacheControl!.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<AuctionSellerResultResponse>())!;
        Assert.Equal(r.SellerUserId,result.UserId); Assert.Equal(3,result.Deliveries.Count); Assert.Empty(_push.Messages); Assert.DoesNotContain(result.Deliveries,d => d.Channel == "Email");
        Assert.True(await Process()); Assert.True(await Process()); Assert.False(await Process()); Assert.Empty(_sender.Messages);
        Assert.All(_push.Messages,m => { Assert.Equal(r.SellerUserId,m.UserId); Assert.Equal(result.NotificationId,m.NotificationId); Assert.Contains("150,000.50",m.Body); Assert.DoesNotContain("đã thanh toán",m.Body); Assert.DoesNotContain("giao hàng",m.Body); });
        var item = Assert.Single((await Inbox(r.SellerUserId.Value)).Items); Assert.Empty((await Inbox(r.WinnerUserId.Value)).Items);
        Assert.Contains("<b>Product</b>",item.Body); Assert.DoesNotContain(r.WinnerUserId.ToString()!,item.Payload); Assert.DoesNotContain(r.SellerUserId.ToString()!,item.Payload);
        using var payload = JsonDocument.Parse(item.Payload!); Assert.Equal("WinnerSelected",payload.RootElement.GetProperty("outcome").GetString()); Assert.Equal(r.OrderId,payload.RootElement.GetProperty("orderId").GetGuid());
        await using var db = Db(); Assert.Single(await db.AuctionSellerResultDispatches.ToListAsync()); Assert.False(db.Database.HasPendingModelChanges());
        var audit = Assert.Single(await db.AuditLogs.Where(x => x.Action == AuditAction.AuctionSellerResultAccepted).ToListAsync()); Assert.DoesNotContain(r.WinnerUserId.ToString()!,audit.NewValues); Assert.Equal("Completed",(await Accept(r)).NotificationStatus);
    }

    [PostgresFact]
    public async Task NoBids_NotifiesSellerWithoutInventingOrderPriceOrReleasedStock()
    {
        var r = Request(true); await Device(r.SellerUserId!.Value); var result = await Accept(r); Assert.True(await Process());
        var item = Assert.Single((await Inbox(r.SellerUserId.Value)).Items); Assert.Contains("không có lượt đặt giá",item.Body); Assert.DoesNotContain("nhả kho",item.Body); Assert.DoesNotContain("tạo lại",item.Body);
        using var payload = JsonDocument.Parse(item.Payload!); Assert.Equal("NoBids",payload.RootElement.GetProperty("outcome").GetString()); Assert.Equal(JsonValueKind.Null,payload.RootElement.GetProperty("orderId").ValueKind); Assert.Equal(JsonValueKind.Null,payload.RootElement.GetProperty("winningAmount").ValueKind);
        Assert.Single(_push.Messages); Assert.Empty(_sender.Messages); Assert.Equal(2,result.Deliveries.Count);
    }

    [PostgresFact]
    public async Task ConditionalInput_InvalidIdsMoneyTimeOrOutcomeRejectedWithoutWrites()
    {
        var w = Request(); var n = Request(true);
        var invalid = new[] { w with { SourceEventId = Guid.Empty },w with { AuctionId = Guid.Empty },w with { ResultId = Guid.Empty },w with { SellerUserId = Guid.Empty },
            w with { WinnerUserId = w.SellerUserId },w with { WinningBidId = null },w with { OrderId = null },w with { WinnerUserId = Guid.Empty },w with { OrderId = Guid.Empty },
            w with { WinningAmount = 0 },w with { WinningAmount = 0.001m },w with { WinningAmount = 10000000000000000m },w with { Currency = "USD" },w with { Currency = null },
            w with { PaymentDeadline = w.DecidedAt },w with { PaymentDeadline = null },w with { DecidedAt = null },w with { DecidedAt = _clock.GetUtcNow().AddMinutes(1) },
            w with { Outcome = "Paid" },w with { AuctionTitle = "\nprivate" },w with { TraceId = "\n" },
            n with { WinningBidId = Guid.NewGuid() },n with { WinnerUserId = Guid.NewGuid() },n with { OrderId = Guid.NewGuid() },n with { WinningAmount = 0 },n with { Currency = "VND" },n with { PaymentDeadline = _clock.GetUtcNow() } };
        foreach(var r in invalid) await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await using var db = Db(); Assert.Empty(await db.AuctionSellerResultDispatches.ToListAsync()); Assert.Empty(await db.Notifications.Where(x => x.NotificationType == NotificationType.AuctionSellerResult).ToListAsync());
    }

    [PostgresFact]
    public async Task AuthStrictJsonAndBodyLimit_RejectClientAndSensitiveFields()
    {
        var r = Request(); Role(null); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Unauthorized,"UNAUTHORIZED");
        foreach(var role in new[] { "buyer","seller","staff" }) { Role(role); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Forbidden,"FORBIDDEN"); }
        Role("service");
        foreach(var field in new[] { "winnerEmail","phone","paymentUrl","paid","penaltyAmount","password","otpCode","bankAccount","stockReleased" })
        {
            var json = JsonSerializer.Serialize(r); json = json[..^1]+",\""+field+"\":\"private\"}";
            await Check(await _client.PostAsync(Route,new StringContent(json,Encoding.UTF8,"application/json")),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        }
        await Check(await _client.PostAsJsonAsync(Route,r with { AuctionTitle = new string('x',17000) }),HttpStatusCode.RequestEntityTooLarge,"REQUEST_BODY_TOO_LARGE");
        Role("admin"); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task HistoricalDeadlineAndCanonicalReplay_DoNotResetOrAddDevices()
    {
        var r = Request() with { DecidedAt = _clock.GetUtcNow().AddDays(-3),PaymentDeadline = _clock.GetUtcNow().AddDays(-2) }; await Device(r.SellerUserId!.Value);
        var first = await Accept(r); await Device(r.SellerUserId.Value);
        var replay = await Accept(r with { AuctionTitle = "  "+r.AuctionTitle+" ",Outcome = "winnerselected",Currency = "vnd",DecidedAt = r.DecidedAt.Value.ToOffset(TimeSpan.FromHours(7)),PaymentDeadline = r.PaymentDeadline.Value.ToOffset(TimeSpan.FromHours(7)),TraceId = "retry" });
        Assert.True(replay.IsReplay); Assert.Equal(first.NotificationId,replay.NotificationId); Assert.Equal(first.Deliveries.Select(x => x.Id),replay.Deliveries.Select(x => x.Id));
        var item = Assert.Single((await Inbox(r.SellerUserId.Value)).Items); using var payload = JsonDocument.Parse(item.Payload!);
        Assert.Equal(r.PaymentDeadline.Value.UtcTicks/10*10,payload.RootElement.GetProperty("paymentDeadline").GetDateTimeOffset().UtcTicks); Assert.Contains("hạn thanh toán gốc",item.Body);
    }

    [PostgresFact]
    public async Task ChangedSnapshotOrRecipient_ConflictsInsteadOfOverwriting()
    {
        var r = Request(); await Accept(r);
        foreach(var changed in new[] { r with { SellerUserId = Guid.NewGuid() },r with { WinnerUserId = Guid.NewGuid() },r with { OrderId = Guid.NewGuid() },r with { AuctionTitle = "changed" },r with { WinningAmount = 1 },r with { PaymentDeadline = r.PaymentDeadline!.Value.AddMinutes(1) },Request(true) with { SourceEventId = r.SourceEventId } })
        { var e = await Assert.ThrowsAsync<DomainException>(() => Accept(changed)); Assert.Equal("AUCTION_SELLER_RESULT_CONFLICT",e.ErrorCode); }
    }

    [PostgresFact]
    public async Task HiddenReceipt_RetainsAllOutcomeIdentityKeys()
    {
        var r = Request(); var result = await Accept(r);
        await using(var db = Db()) { await db.AuctionSellerResultDispatches.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted,true)); await db.Notifications.Where(x => x.Id == result.NotificationId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted,true)); }
        Assert.True((await Accept(r)).IsReplay);
        foreach(var alias in new[] { Request() with { AuctionId = r.AuctionId },Request() with { ResultId = r.ResultId },Request() with { WinningBidId = r.WinningBidId },Request() with { OrderId = r.OrderId },Request(true) with { AuctionId = r.AuctionId } })
        { var e = await Assert.ThrowsAsync<DomainException>(() => Accept(alias)); Assert.Equal("AUCTION_SELLER_RESULT_ALREADY_NOTIFIED",e.ErrorCode); }
    }

    [PostgresFact]
    public async Task ConcurrentSameEvent_AcceptsOnce_ChangedRecipientConflicts()
    {
        var r = Request(); var replies = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Accept(r))); Assert.Single(replies,x => !x.IsReplay); Assert.Single(replies.Select(x => x.NotificationId).Distinct());
        var other = Request(); var statuses = await Task.WhenAll(new[] { other,other with { SellerUserId = Guid.NewGuid() } }.Select(async x => { try { await Accept(x); return 200; } catch(DomainException ex) { Assert.Equal("AUCTION_SELLER_RESULT_CONFLICT",ex.ErrorCode); return ex.StatusCode; } })); Assert.Single(statuses,x => x == 200); Assert.Single(statuses,x => x == 409);
    }

    [PostgresFact]
    public async Task ConcurrentConflictingOutcomes_OneInitialResultPerAuction()
    {
        var winner = Request(); var noBids = Request(true) with { AuctionId = winner.AuctionId,ResultId = winner.ResultId,SellerUserId = winner.SellerUserId };
        var statuses = await Task.WhenAll(new[] { winner,noBids }.Select(async r => { try { await Accept(r); return 200; } catch(DomainException e) { Assert.Equal("AUCTION_SELLER_RESULT_ALREADY_NOTIFIED",e.ErrorCode); return e.StatusCode; } }));
        Assert.Single(statuses,x => x == 200); Assert.Single(statuses,x => x == 409); await using var db = Db(); Assert.Single(await db.AuctionSellerResultDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task RecreatedAuctionsAndNoBidNullableKeys_AllowSameSellerAgain()
    {
        var r = Request(true); var first = await Accept(r); var second = await Accept(Request(true) with { SellerUserId = r.SellerUserId }); var third = await Accept(Request() with { SellerUserId = r.SellerUserId });
        Assert.Equal(3,new[] { first.NotificationId,second.NotificationId,third.NotificationId }.Distinct().Count()); Assert.Equal(3,(await Inbox(r.SellerUserId!.Value)).Items.Count);
    }

    [PostgresFact]
    public async Task OptionalPreferences_SuppressEachChannelIndependently()
    {
        var a = Request(); await Device(a.SellerUserId!.Value); await Preference(a.SellerUserId.Value,ChannelType.Push); var ar = await Accept(a);
        Assert.Contains(ar.Deliveries,x => x.Channel == "Push" && x.ErrorCode == "PREFERENCE_DISABLED"); Assert.True(ar.InboxVisible); Assert.False(await Process());
        var b = Request(true); await Device(b.SellerUserId!.Value); await Preference(b.SellerUserId.Value,ChannelType.InApp); var br = await Accept(b);
        Assert.False(br.InboxVisible); Assert.True(await Process()); Assert.Equal(b.SellerUserId,Assert.Single(_push.Messages).UserId); Assert.Empty((await Inbox(b.SellerUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task MandatoryPairOverridesOnlyOwnChannel_GlobalDisableStillWins()
    {
        var r = Request(); await Device(r.SellerUserId!.Value); await Preference(r.SellerUserId.Value,ChannelType.Push); await Preference(r.SellerUserId.Value,ChannelType.InApp);
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_SELLER_WINNER_SELECTED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory,true));
        var result = await Accept(r); Assert.False(result.InboxVisible); Assert.Contains(result.Deliveries,x => x.Channel == "Push" && x.Status == "Pending");
        await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var other = Request() with { SellerUserId = r.SellerUserId }; var disabled = await Accept(other); Assert.Contains(disabled.Deliveries,x => x.Channel == "Push" && x.ErrorCode == "CHANNEL_DISABLED");
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
        Assert.Empty((await Inbox(r.SellerUserId.Value,true)).Items); Role("buyer",r.WinnerUserId); await Check(await _client.GetAsync(path),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
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
        var b = Request(true); await Device(b.SellerUserId!.Value,101); Assert.Contains((await Accept(b)).Deliveries,d => d.ErrorCode == "PUSH_DEVICE_LIMIT");
        Assert.Single((await Inbox(a.SellerUserId!.Value)).Items); Assert.Single((await Inbox(b.SellerUserId.Value)).Items); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task PushFailureAndAutomaticRetry_KeepOriginalSnapshotAndSuccessfulInbox()
    {
        var r = Request(); await Device(r.SellerUserId!.Value); _push.Result = _ => PushDeliveryResult.Failure("FCM_UNAVAILABLE","Test transient failure.",true); var accepted = await Accept(r); Assert.True(await Process()); var original = Assert.Single(_push.Messages).Body;
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_SELLER_WINNER_SELECTED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"Changed later"));
        _clock.Advance(TimeSpan.FromSeconds(10)); _push.Result = _ => PushDeliveryResult.Sent("ok"); Assert.True(await Process()); Assert.Equal(original,_push.Messages.Last().Body); Assert.Single((await Inbox(r.SellerUserId.Value)).Items); Assert.Equal("Completed",(await Accept(r)).NotificationStatus);
        var other = Request(true); await Device(other.SellerUserId!.Value); await Accept(other); _push.Result = _ => PushDeliveryResult.Failure("FCM_AUTH_FAILED","Test permanent failure."); Assert.True(await Process()); Assert.Equal("Partial",(await Accept(other)).NotificationStatus); Assert.Single((await Inbox(other.SellerUserId.Value)).Items);
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
        await using(var db = Db()) { Assert.Empty(await db.AuctionSellerResultDispatches.ToListAsync()); Assert.Empty(await db.Notifications.Where(n => n.SourceEventId == r.SourceEventId).ToListAsync()); Assert.Empty(await db.AuditLogs.Where(a => a.Action == AuditAction.AuctionSellerResultAccepted).ToListAsync()); }
        Assert.False((await Accept(r)).IsReplay);
    }

    [PostgresFact]
    public async Task TemplateAndChannelErrors_RejectAcceptance()
    {
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_SELLER_NO_BIDS_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,false));
        await Check(await _client.PostAsJsonAsync(Route,Request(true)),HttpStatusCode.NotFound,"TEMPLATE_NOT_FOUND");
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_SELLER_WINNER_SELECTED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.ChannelCode,ChannelType.Email));
        await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.Conflict,"AUCTION_SELLER_RESULT_TEMPLATE_INVALID");
        await using(var db = Db()) { await db.NotificationTemplates.Where(t => t.Code == "AUCTION_SELLER_WINNER_SELECTED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.ChannelCode,ChannelType.Push)); await db.NotificationChannels.Where(c => c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted,true)); }
        await Check(await _client.PostAsJsonAsync(Route,Request()),HttpStatusCode.Conflict,"CHANNEL_NOT_FOUND"); await using var verify = Db(); Assert.Empty(await verify.AuctionSellerResultDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task TemplateMissingDataOrMalformedBody_IsAtomic()
    {
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_SELLER_NO_BIDS_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{WinnerEmail}}"));
        await Check(await _client.PostAsJsonAsync(Route,Request(true)),HttpStatusCode.BadRequest,"TEMPLATE_DATA_MISSING");
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_SELLER_NO_BIDS_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{AuctionTitle"));
        await Check(await _client.PostAsJsonAsync(Route,Request(true)),HttpStatusCode.Conflict,"TEMPLATE_INVALID"); await using var verify = Db(); Assert.Empty(await verify.AuctionSellerResultDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task TemplateSharedLock_PreventsMetadataEditBeforeAcceptanceCommit()
    {
        var r = Request(); var gate = new GateSave(); await using var acceptingDb = Db(gate); var accepting = Service(acceptingDb).CreateAsync(r); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var editor = Db(); await using var tx = await editor.Database.BeginTransactionAsync(); await editor.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => editor.NotificationTemplates.Where(t => t.Code == "AUCTION_SELLER_WINNER_SELECTED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.NotificationType,NotificationType.Promotion))); Assert.Equal("55P03",Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        finally { gate.Release.TrySetResult(); }
        Assert.False((await accepting.WaitAsync(TimeSpan.FromSeconds(5))).IsReplay);
    }

    [PostgresFact]
    public async Task GenericDispatchCollision_IsNotAdoptedAsSellerResult()
    {
        var r = Request(); await using(var db = Db()) await new DispatchService(db).CreateAsync(new()
        { SourceEventId = r.SourceEventId,UserId = r.SellerUserId,TemplateCode = "AUCTION_SELLER_WINNER_SELECTED_PUSH",Data = new() { ["AuctionTitle"] = "Other",["DecidedAtUtc"] = "Other",["WinningAmount"] = "Other",["PaymentDeadlineUtc"] = "Other" } });
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"AUCTION_SELLER_RESULT_CONFLICT"); await using var verify = Db(); Assert.Empty(await verify.AuctionSellerResultDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task WinnerAndSeller_SameSourceEventAreIndependentReceiptsAndOwnedItems()
    {
        var r = Request(); await using(var db = Db()) await new Notification.Application.Services.AuctionWinners.AuctionWinnerService(db,_clock).CreateAsync(new()
        { SourceEventId = r.SourceEventId,AuctionId = r.AuctionId,ResultId = r.ResultId,WinningBidId = r.WinningBidId,WinnerUserId = r.WinnerUserId,SellerUserId = r.SellerUserId,OrderId = r.OrderId,WinnerEmail = "winner@example.com",AuctionTitle = r.AuctionTitle,WinningAmount = r.WinningAmount,Currency = "VND",SettledAt = r.DecidedAt,PaymentDeadline = r.PaymentDeadline });
        await Accept(r); Assert.Single((await Inbox(r.SellerUserId!.Value)).Items); Assert.Single((await Inbox(r.WinnerUserId!.Value)).Items);
        await using var verify = Db(); Assert.Single(await verify.AuctionWinnerDispatches.ToListAsync()); Assert.Single(await verify.AuctionSellerResultDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task SwaggerAndPreferences_ExposeNewOptionalTypeWithoutChangingExistingEnums()
    {
        var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route,swagger); Assert.Contains("{notificationId}",swagger); Assert.Contains("decidedAt",swagger);
        Role("seller"); var prefs = (await _client.GetFromJsonAsync<List<NotificationPreferenceResponse>>("/api/v1/notifications/preferences"))!;
        Assert.Equal(54,prefs.Count); Assert.Equal(18,prefs.Count(p => p.IsMandatory)); Assert.All(prefs.Where(p => p.NotificationType == "AuctionSellerResult"),p => Assert.False(p.IsMandatory));
        Assert.Equal(10,(int)NotificationType.AuctionSellerResult); Assert.Equal(9,(int)NotificationType.AuctionPaymentTimedOut); Assert.Equal(19,(int)AuditAction.AuctionSellerResultAccepted);
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
        db.NotificationPreferences.Add(new() { Id = Guid.NewGuid(),UserId = owner,NotificationType = NotificationType.AuctionSellerResult,ChannelCode = channel,IsDisabled = disabled });
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
