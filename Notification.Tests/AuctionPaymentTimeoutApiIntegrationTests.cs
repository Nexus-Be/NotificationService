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
using Notification.Application.Services.AuctionPaymentTimeouts;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class AuctionPaymentTimeoutApiIntegrationTests : IAsyncLifetime
{
    private const string Route = "/api/v1/notifications/auction-payment-timeouts";
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
    private AuctionPaymentTimeoutService Service(AppDbContext db) => new(db,_clock);
    private CreateAuctionPaymentTimeoutRequest Request() => new()
    {
        SourceEventId = Guid.NewGuid(),AuctionId = Guid.NewGuid(),ResultId = Guid.NewGuid(),WinningBidId = Guid.NewGuid(),OrderId = Guid.NewGuid(),WinnerUserId = Guid.NewGuid(),SellerUserId = Guid.NewGuid(),
        WinnerEmail = "winner@example.com",WinnerFullName = "<b>Winner</b>",AuctionTitle = "<script>Product</script>",OrderStatus = "Cancelled",AuctionStatus = "Failed",
        SettledAt = _clock.GetUtcNow().AddHours(-25),PaymentDeadline = _clock.GetUtcNow().AddHours(-1),TimedOutAt = _clock.GetUtcNow().AddSeconds(-30)
    };
    private async Task<AuctionPaymentTimeoutResponse> Accept(CreateAuctionPaymentTimeoutRequest request)
    { await using var db = Db(); return await Service(db).CreateAsync(request); }
    private async Task<bool> Process()
    { await using var db = Db(); return await new DeliveryService(db,_sender,new(),_clock,_push).ProcessNextAsync(); }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_auction_timeout_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(AuctionPaymentTimeoutsController).Assembly)
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
    public async Task Http_QueuesOneWinnerItemWithEmailAndPush_PrivatePayloadAndNoPenaltyClaim()
    {
        var r = Request(); await Device(r.WinnerUserId!.Value,2); await Device(r.SellerUserId!.Value);
        var response = await _client.PostAsJsonAsync(Route,r); await Check(response,HttpStatusCode.OK); Assert.True(response.Headers.CacheControl!.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<AuctionPaymentTimeoutResponse>())!; Assert.Equal(r.OrderId,result.OrderId); Assert.Equal(4,result.Deliveries.Count); Assert.Empty(_sender.Messages);
        for(var i = 0;i < 3;i++) Assert.True(await Process()); Assert.False(await Process());
        var email = Assert.Single(_sender.Messages); Assert.Equal(r.WinnerEmail,email.Recipient); Assert.Contains("&lt;b&gt;Winner&lt;/b&gt;",email.HtmlBody); Assert.Contains("&lt;script&gt;Product&lt;/script&gt;",email.HtmlBody); Assert.DoesNotContain("<script>",email.HtmlBody);
        Assert.Contains("đơn đã bị hủy",email.HtmlBody); Assert.Contains("không yêu cầu thanh toán khoản phạt tiền",email.HtmlBody); Assert.DoesNotContain("đã trừ",email.HtmlBody); Assert.DoesNotContain("href=",email.HtmlBody);
        Assert.Equal(2,_push.Messages.Count); Assert.All(_push.Messages,m => { Assert.Equal(r.WinnerUserId,m.UserId); Assert.Equal(result.NotificationId,m.NotificationId); Assert.DoesNotContain("<p>",m.Body); });
        var item = Assert.Single((await Inbox(r.WinnerUserId.Value)).Items); Assert.Empty((await Inbox(r.SellerUserId.Value)).Items); Assert.DoesNotContain(r.WinnerEmail,item.Payload); Assert.DoesNotContain(r.SellerUserId.ToString()!,item.Payload);
        using var payload = JsonDocument.Parse(item.Payload!); Assert.Equal("Cancelled",payload.RootElement.GetProperty("orderStatus").GetString()); Assert.Equal("Failed",payload.RootElement.GetProperty("auctionStatus").GetString()); Assert.False(payload.RootElement.TryGetProperty("penaltyAmount",out _));
        await using var db = Db(); Assert.Single(await db.AuctionPaymentTimeoutDispatches.ToListAsync()); Assert.False(db.Database.HasPendingModelChanges());
        var audit = Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditAction.AuctionPaymentTimeoutAccepted).ToListAsync()); Assert.DoesNotContain(r.WinnerEmail,audit.NewValues);
    }

    [PostgresFact]
    public async Task PrematureDeadline_WrongStatesOrInvertedTimes_AreRejectedWithoutAnyWrites()
    {
        var r = Request();
        foreach(var invalid in new[] { r with { PaymentDeadline = _clock.GetUtcNow().AddSeconds(5),TimedOutAt = _clock.GetUtcNow().AddSeconds(6) },
            r with { TimedOutAt = r.PaymentDeadline!.Value.AddSeconds(-1) },r with { PaymentDeadline = r.SettledAt },r with { TimedOutAt = _clock.GetUtcNow().AddMinutes(1) },
            r with { OrderStatus = "Paid" },r with { OrderStatus = "Pending" },r with { AuctionStatus = "Running" },r with { AuctionStatus = "Cancelled" },
            r with { OrderStatus = "0" },r with { AuctionStatus = "0" } })
            await Check(await _client.PostAsJsonAsync(Route,invalid),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        await using var db = Db(); Assert.Empty(await db.AuctionPaymentTimeoutDispatches.ToListAsync()); Assert.False(await db.Notifications.AnyAsync(n => n.SourceEventId == r.SourceEventId));
    }

    [PostgresFact]
    public async Task AtDeadlineBoundary_IsAllowedOnlyWithConfirmedCancelledFailedStates()
    {
        var r = Request() with { PaymentDeadline = _clock.GetUtcNow(),TimedOutAt = _clock.GetUtcNow() };
        Assert.False((await Accept(r)).IsReplay);
    }

    [PostgresFact]
    public async Task HistoricalOutcome_PreservesOriginalDeadlineAndDoesNotNeedPriorWinnerReceipt()
    {
        var r = Request() with { SettledAt = _clock.GetUtcNow().AddDays(-4),PaymentDeadline = _clock.GetUtcNow().AddDays(-3),TimedOutAt = _clock.GetUtcNow().AddDays(-2) };
        await Accept(r); var item = Assert.Single((await Inbox(r.WinnerUserId!.Value)).Items); using var payload = JsonDocument.Parse(item.Payload!);
        Assert.Equal(new DateTimeOffset(r.PaymentDeadline!.Value.UtcTicks / 10 * 10,TimeSpan.Zero),payload.RootElement.GetProperty("paymentDeadline").GetDateTimeOffset());
        await using var db = Db(); Assert.Empty(await db.AuctionWinnerDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task Http_RejectsUntrustedRolesInvalidIdsContactAndUnknownFeeFields()
    {
        var r = Request(); Role(null); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Unauthorized);
        foreach(var role in new[] { "buyer","seller","staff" }) { Role(role); await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Forbidden); }
        Role("admin");
        foreach(var invalid in new[] { r with { SourceEventId = null },r with { AuctionId = Guid.Empty },r with { ResultId = null },r with { WinningBidId = Guid.Empty },r with { OrderId = null },
            r with { WinnerUserId = r.SellerUserId },r with { WinnerEmail = "invalid" },r with { WinnerFullName = "bad\nname" },r with { AuctionTitle = new string('a',201) },r with { SettledAt = null },
            r with { PaymentDeadline = null },r with { TimedOutAt = null },r with { TraceId = new string('a',101) } })
            await Check(await _client.PostAsJsonAsync(Route,invalid),HttpStatusCode.BadRequest,"VAL_INVALID_INPUT");
        foreach(var field in new[] { "penaltyAmount","reputationPointsDeducted","paymentUrl","token","password" })
        {
            var body = JsonSerializer.SerializeToNode(r)!.AsObject(); body[field] = "do-not-accept-sensitive-value";
            var response = await _client.PostAsync(Route,new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json")); await Check(response,HttpStatusCode.BadRequest,"VAL_INVALID_INPUT"); Assert.DoesNotContain("do-not-accept-sensitive-value",await response.Content.ReadAsStringAsync());
        }
        await Check(await _client.PostAsync(Route,new StringContent("{\"auctionTitle\":\"" + new string('a',17000) + "\"}",Encoding.UTF8,"application/json")),HttpStatusCode.RequestEntityTooLarge,"REQUEST_BODY_TOO_LARGE");
    }

    [PostgresFact]
    public async Task MandatoryEmailIgnoresLegacyOptOut_WhileOtherChannelsRemainOptional()
    {
        var r = Request(); await Device(r.WinnerUserId!.Value); foreach(var c in new[] { ChannelType.Email,ChannelType.Push,ChannelType.InApp }) await Preference(r.WinnerUserId.Value,c);
        var result = await Accept(r); Assert.False(result.InboxVisible); Assert.Contains(result.Deliveries,d => d.Channel == "Email" && d.Status == "Pending"); Assert.Contains(result.Deliveries,d => d.Channel == "Push" && d.ErrorCode == "PREFERENCE_DISABLED");
        Assert.True(await Process()); Assert.Single(_sender.Messages); Assert.Empty(_push.Messages); Assert.Empty((await Inbox(r.WinnerUserId.Value)).Items); await Check(await _client.GetAsync(Route + "/" + result.NotificationId),HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task GlobalEmailDisable_WinsMandatoryButPreservesPushAndInbox()
    {
        var r = Request(); await Device(r.WinnerUserId!.Value); await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var result = await Accept(r); Assert.Contains(result.Deliveries,d => d.Channel == "Email" && d.ErrorCode == "CHANNEL_DISABLED"); await Process(); Assert.Empty(_sender.Messages); Assert.Single(_push.Messages); Assert.Single((await Inbox(r.WinnerUserId.Value)).Items);
        await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,false)); Assert.True((await Accept(r)).IsReplay); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task GlobalInAppDisable_StillAllowsEmailPushAndOwnerDetail()
    {
        var r = Request(); await Device(r.WinnerUserId!.Value); await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var result = await Accept(r); Assert.False(result.InboxVisible); await Process(); await Process(); Assert.Single(_sender.Messages); Assert.Single(_push.Messages); Assert.Empty((await Inbox(r.WinnerUserId.Value)).Items); await Check(await _client.GetAsync(Route + "/" + result.NotificationId),HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task AllChannelsDisabled_HideItemAndDoNotCallProviders()
    {
        var r = Request(); await using(var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Email || c.Code == ChannelType.Push || c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled,true));
        var result = await Accept(r); Assert.All(result.Deliveries,d => Assert.Equal("Skipped",d.Status)); Assert.False(result.InboxVisible); Assert.False(await Process()); Assert.Empty((await Inbox(r.WinnerUserId!.Value)).Items);
    }

    [PostgresFact]
    public async Task NoOrTooManyDevices_OnlySkipsPushAndKeepsEmailInbox()
    {
        var a = Request(); Assert.Contains((await Accept(a)).Deliveries,d => d.ErrorCode == "PUSH_NO_ACTIVE_DEVICES");
        var b = Request(); await Device(b.WinnerUserId!.Value,101); Assert.Contains((await Accept(b)).Deliveries,d => d.ErrorCode == "PUSH_DEVICE_LIMIT");
        await Process(); await Process(); Assert.Equal(2,_sender.Messages.Count); Assert.Empty(_push.Messages); Assert.Single((await Inbox(a.WinnerUserId!.Value)).Items); Assert.Single((await Inbox(b.WinnerUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task EmailFailure_DoesNotLoseInboxOrSuccessfulPush()
    {
        var r = Request(); await Device(r.WinnerUserId!.Value); _sender.Result = EmailDeliveryResult.Failure("SMTP_NOT_CONFIGURED","Test permanent failure.");
        await Accept(r); await Process(); await Process(); Assert.Equal("Partial",(await Accept(r)).NotificationStatus); Assert.Single(_push.Messages); Assert.Single((await Inbox(r.WinnerUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task PushFailure_DoesNotBlockRequiredEmailOrEraseInbox()
    {
        var r = Request(); await Device(r.WinnerUserId!.Value); _push.Result = _ => PushDeliveryResult.Failure("FCM_AUTH_FAILED","Test permanent failure.");
        await Accept(r); await Process(); await Process(); Assert.Equal("Partial",(await Accept(r)).NotificationStatus); Assert.Single(_sender.Messages); Assert.Single((await Inbox(r.WinnerUserId.Value)).Items);
    }

    [PostgresFact]
    public async Task EmailRetry_PreservesSnapshotAfterTemplateEditsAndDoesNotResendPush()
    {
        var r = Request(); await Device(r.WinnerUserId!.Value); _sender.Result = EmailDeliveryResult.Failure("SMTP_450","Test transient failure.",true);
        await Accept(r); await Process(); await Process(); var original = Assert.Single(_sender.Messages).HtmlBody;
        await using(var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "AUCTION_PAYMENT_TIMEOUT_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"Changed after accept"));
        _clock.Advance(TimeSpan.FromSeconds(10)); _sender.Result = EmailDeliveryResult.Sent(); Assert.True(await Process()); Assert.Equal(original,_sender.Messages.Last().HtmlBody); Assert.Single(_push.Messages);
    }

    [PostgresFact]
    public async Task OwnerDetailAndReadBadge_RejectSellerAndForeignUser()
    {
        var r = Request(); var result = await Accept(r); Role(null); await Check(await _client.GetAsync(Route + "/" + result.NotificationId),HttpStatusCode.Unauthorized);
        Role("seller",r.SellerUserId); await Check(await _client.GetAsync(Route + "/" + result.NotificationId),HttpStatusCode.NotFound,"NOTIFICATION_NOT_FOUND");
        Role("buyer",r.WinnerUserId); var detail = await _client.GetAsync(Route + "/" + result.NotificationId); await Check(detail,HttpStatusCode.OK); Assert.True(detail.Headers.CacheControl!.NoStore);
        await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + result.NotificationId + "/read-states",new { isRead = true }),HttpStatusCode.OK); Assert.Empty((await Inbox(r.WinnerUserId!.Value,true)).Items);
        await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + result.NotificationId + "/read-states",new { isRead = false }),HttpStatusCode.OK); Assert.Single((await Inbox(r.WinnerUserId.Value,true)).Items);
    }

    [PostgresFact]
    public async Task Replay_NormalizesHeaderStatesAndTrace_WithoutNewFanoutOrMutableTemplates()
    {
        var r = Request(); var first = await Accept(r); await Process(); await Device(r.WinnerUserId!.Value);
        await using(var db = Db()) { await db.NotificationTemplates.Where(t => t.Code.StartsWith("AUCTION_PAYMENT_TIMEOUT_")).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,false)); await db.AuctionPaymentTimeoutDispatches.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted,true)); }
        var replay = await Accept(r with { WinnerEmail = " WINNER@EXAMPLE.COM ",WinnerFullName = " <b>Winner</b> ",AuctionTitle = " " + r.AuctionTitle + " ",OrderStatus = "cancelled",AuctionStatus = "failed",SettledAt = r.SettledAt!.Value.ToOffset(TimeSpan.FromHours(7)),TraceId = "retry" });
        Assert.True(replay.IsReplay); Assert.Equal(first.NotificationId,replay.NotificationId); Assert.False(await Process()); Assert.Single(_sender.Messages);
    }

    [PostgresFact]
    public async Task SameEventChangedTargetOrderDeadlineOrOutcomeTime_Conflicts()
    {
        var r = Request(); await Accept(r);
        foreach(var conflict in new[] { r with { WinnerUserId = Guid.NewGuid() },r with { WinnerEmail = "other@example.com" },r with { OrderId = Guid.NewGuid() },r with { ResultId = Guid.NewGuid() },
            r with { PaymentDeadline = r.PaymentDeadline!.Value.AddMinutes(-1) },r with { TimedOutAt = r.TimedOutAt!.Value.AddSeconds(-1) } })
            await Check(await _client.PostAsJsonAsync(Route,conflict),HttpStatusCode.Conflict,"AUCTION_PAYMENT_TIMEOUT_CONFLICT");
    }

    [PostgresFact]
    public async Task ConcurrentSameEvent_AcceptsOnceAndReplaysSevenTimes()
    {
        var r = Request(); var results = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Accept(r))).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results,x => !x.IsReplay); Assert.Single(results.Select(x => x.NotificationId).Distinct()); await using var db = Db(); Assert.Single(await db.AuctionPaymentTimeoutDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentEventAliasesForSameOutcome_OnlyOneCanAccept()
    {
        var r = Request(); var statuses = await Task.WhenAll(new[] { r,r with { SourceEventId = Guid.NewGuid() } }.Select(async x =>
        { try { await Accept(x); return 200; } catch(DomainException ex) { Assert.Equal("AUCTION_PAYMENT_TIMEOUT_ALREADY_NOTIFIED",ex.ErrorCode); return ex.StatusCode; } }));
        Assert.Single(statuses,x => x == 200); Assert.Single(statuses,x => x == 409);
    }

    [PostgresFact]
    public async Task ReusedOrderResultBidOrAuction_CannotBypassOutcomeDedup()
    {
        var r = Request(); await Accept(r); await using(var db = Db()) await db.AuctionPaymentTimeoutDispatches.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted,true));
        foreach(var duplicate in new[] { Request() with { OrderId = r.OrderId },Request() with { AuctionId = r.AuctionId },Request() with { ResultId = r.ResultId },Request() with { WinningBidId = r.WinningBidId } })
            await Check(await _client.PostAsJsonAsync(Route,duplicate),HttpStatusCode.Conflict,"AUCTION_PAYMENT_TIMEOUT_ALREADY_NOTIFIED");
    }

    [PostgresFact]
    public async Task FreshAuctionOrderAndResult_CanNotifySameWinnerAgain()
    {
        var r = Request(); await Accept(r); await Accept(Request() with { WinnerUserId = r.WinnerUserId }); Assert.Equal(2,(await Inbox(r.WinnerUserId!.Value)).Pagination.TotalCount);
    }

    [PostgresFact]
    public async Task AuditFailure_RollsBackEveryChannelReceiptAndOutcomeReservation()
    {
        var r = Request(); await using(var db = Db(new FailAllSave())) await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(r));
        await using var verify = Db(); Assert.Empty(await verify.AuctionPaymentTimeoutDispatches.ToListAsync()); Assert.False(await verify.Notifications.AnyAsync(n => n.SourceEventId == r.SourceEventId)); Assert.False((await Accept(r)).IsReplay);
    }

    [PostgresFact]
    public async Task InvalidTemplatesInactiveOrMissingChannels_AcceptNoGroup()
    {
        var r = Request(); await using var db = Db(); await db.NotificationTemplates.Where(t => t.Code == "AUCTION_PAYMENT_TIMEOUT_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory,false));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"AUCTION_PAYMENT_TIMEOUT_TEMPLATE_INVALID");
        await db.NotificationTemplates.Where(t => t.Code == "AUCTION_PAYMENT_TIMEOUT_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory,true).SetProperty(t => t.IsActive,false));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.NotFound,"TEMPLATE_NOT_FOUND");
        await db.NotificationTemplates.Where(t => t.Code == "AUCTION_PAYMENT_TIMEOUT_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive,true));
        await db.NotificationChannels.Where(c => c.Code == ChannelType.Email).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted,true));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"CHANNEL_NOT_FOUND"); Assert.Empty(await db.AuctionPaymentTimeoutDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task MissingDataAndMalformedTemplate_RollBackRatherThanAcceptUnverifiedPenaltyFields()
    {
        var r = Request(); await using var db = Db(); await db.NotificationTemplates.Where(t => t.Code == "AUCTION_PAYMENT_TIMEOUT_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{PenaltyAmount}}"));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.BadRequest,"TEMPLATE_DATA_MISSING");
        await db.NotificationTemplates.Where(t => t.Code == "AUCTION_PAYMENT_TIMEOUT_EMAIL").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate,"{{invalid"));
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"TEMPLATE_INVALID"); Assert.Empty(await db.AuctionPaymentTimeoutDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task GenericDispatchCollision_IsNotAdoptedAsVerifiedTimeout()
    {
        var r = Request(); await using(var db = Db()) await new DispatchService(db).CreateAsync(new()
        {
            SourceEventId = r.SourceEventId,UserId = r.WinnerUserId,TemplateCode = "AUCTION_PAYMENT_TIMEOUT_PUSH",
            Data = new() { ["AuctionTitle"] = "Other",["PaymentDeadlineUtc"] = "Other",["TimedOutAtUtc"] = "Other" }
        });
        await Check(await _client.PostAsJsonAsync(Route,r),HttpStatusCode.Conflict,"AUCTION_PAYMENT_TIMEOUT_CONFLICT"); await using var verify = Db(); Assert.Empty(await verify.AuctionPaymentTimeoutDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task SwaggerAndNewTypePreferenceMatrix_MatchTimeoutContract()
    {
        var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route,swagger); Assert.Contains("{notificationId}",swagger); Assert.Contains("timedOutAt",swagger);
        var r = Request(); Role("buyer",r.WinnerUserId); var prefs = (await _client.GetFromJsonAsync<List<NotificationPreferenceResponse>>("/api/v1/notifications/preferences"))!;
        Assert.Equal(54,prefs.Count); Assert.True(prefs.Single(p => p.NotificationType == "AuctionPaymentTimedOut" && p.ChannelCode == "Email").IsMandatory); Assert.False(prefs.Single(p => p.NotificationType == "AuctionPaymentTimedOut" && p.ChannelCode == "Push").IsMandatory);
    }

    private async Task Preference(Guid owner,ChannelType channel,bool disabled = true)
    {
        await using var db = Db();
        db.NotificationPreferences.Add(new() { Id = Guid.NewGuid(),UserId = owner,NotificationType = NotificationType.AuctionPaymentTimedOut,ChannelCode = channel,IsDisabled = disabled });
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
