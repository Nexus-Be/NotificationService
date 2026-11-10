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
using Notification.Application.Services.OrderBuyerCancellations;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class OrderBuyerCancellationApiIntegrationTests : IAsyncLifetime
{
    private const string Route = "/api/v1/notifications/order-buyer-cancellations";
    private string? _admin, _database, _connection;
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
    private OrderBuyerCancellationService Service(AppDbContext db) => new(db, _clock);
    private CreateOrderBuyerCancellationRequest Request() => new()
    {
        SourceEventId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        ShopId = Guid.NewGuid(),
        BuyerUserId = Guid.NewGuid(),
        SellerUserId = Guid.NewGuid(),
        OrderNo = "ORDER-<b>DEMO</b>",
        OrderKind = "Regular",
        PreviousOrderStatus = "Paid",
        OrderStatus = "Cancelled",
        CancelledBy = "Buyer",
        HandoverStatus = "NotHandedOver",
        Reason = "Thay đổi nhu cầu <b>mua hàng</b>",
        OrderCreatedAt = _clock.GetUtcNow().AddDays(-3),
        PaidAt = _clock.GetUtcNow().AddDays(-2),
        CancelledAt = _clock.GetUtcNow().AddMinutes(-1)
    };
    private async Task<OrderBuyerCancellationResponse> Accept(CreateOrderBuyerCancellationRequest request)
    { await using var db = Db(); return await Service(db).CreateAsync(request); }
    private async Task<bool> Process()
    { await using var db = Db(); return await new DeliveryService(db, _sender, new(), _clock, _push).ProcessNextAsync(); }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_order_buyer_cancellation_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"", admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(OrderBuyerCancellationsController).Assembly)
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
        await new NpgsqlCommand("DROP DATABASE \"" + _database + "\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }
    private void Role(string? role, Guid? userId = null)
    {
        _client.DefaultRequestHeaders.Remove("x-user-id"); _client.DefaultRequestHeaders.Remove("x-user-roles");
        if (role is null) return;
        _client.DefaultRequestHeaders.Add("x-user-id", (userId ?? Guid.NewGuid()).ToString()); _client.DefaultRequestHeaders.Add("x-user-roles", role);
    }
    private static async Task Check(HttpResponseMessage response, HttpStatusCode expected, string? code = null)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {text}");
        if (code is not null) Assert.Equal(code, JsonDocument.Parse(text).RootElement.GetProperty("errorCode").GetString());
    }

    [PostgresFact]
    public async Task PaidAndShippingBeforeHandover_CreatePrivatePairWithoutRefundCompletionClaim()
    {
        foreach (var status in new[] { "Paid", "Shipping" })
        {
            Role("service"); var r = Request() with { PreviousOrderStatus = status }; await Device(r.BuyerUserId!.Value); await Device(r.SellerUserId!.Value);
            var response = await _client.PostAsJsonAsync(Route, r); await Check(response, HttpStatusCode.OK); Assert.True(response.Headers.CacheControl!.NoStore);
            var result = (await response.Content.ReadFromJsonAsync<OrderBuyerCancellationResponse>())!; Assert.NotEqual(result.Buyer.NotificationId, result.Seller.NotificationId);
            Assert.True(await Process()); Assert.True(await Process());
            var buyer = Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); var seller = Assert.Single((await Inbox(r.SellerUserId.Value)).Items);
            Assert.Contains("theo yêu cầu của bạn", buyer.Body); Assert.Contains("tiến trình hoàn tiền", buyer.Body); Assert.Contains("Người mua đã hủy", seller.Body); Assert.Contains(r.Reason, buyer.Body);
            foreach (var row in new[] { buyer, seller }) { Assert.DoesNotContain("tiền đã vào ví", row.Body); Assert.DoesNotContain("đã hoàn tiền", row.Body); Assert.DoesNotContain("đã nhả kho", row.Body); Assert.DoesNotContain("đã trừ uy tín", row.Body); }
            Assert.DoesNotContain(r.SellerUserId.ToString()!, buyer.Payload); Assert.DoesNotContain(r.BuyerUserId.ToString()!, seller.Payload);
            using var p = JsonDocument.Parse(buyer.Payload!); Assert.Equal("OrderBuyerCancelled", p.RootElement.GetProperty("type").GetString()); Assert.Equal(status, p.RootElement.GetProperty("previousOrderStatus").GetString()); Assert.Equal("NotHandedOver", p.RootElement.GetProperty("handoverStatus").GetString()); Assert.False(p.RootElement.TryGetProperty("refundStatus", out _));
        }
        await using var db = Db(); Assert.Equal(2, await db.OrderBuyerCancellationDispatches.CountAsync()); Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action == AuditAction.OrderBuyerCancellationAccepted)); Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task UnpaidAuctionShippedNonBuyerAndInvalidTimes_AreRejected()
    {
        var r = Request();
        var invalid = new[] { r with { OrderKind = "Auction" }, r with { PreviousOrderStatus = "PendingPayment" }, r with { PreviousOrderStatus = "Delivered" }, r with { PreviousOrderStatus = "Completed" }, r with { PreviousOrderStatus = "Refunded" }, r with { PreviousOrderStatus = "Cancelled" }, r with { OrderStatus = "Refunded" }, r with { CancelledBy = "Seller" }, r with { CancelledBy = "Admin" }, r with { CancelledBy = "System" }, r with { HandoverStatus = "HandedOver" }, r with { HandoverStatus = "Unknown" }, r with { PaidAt = r.OrderCreatedAt!.Value.AddSeconds(-1) }, r with { CancelledAt = r.PaidAt!.Value.AddSeconds(-1) }, r with { CancelledAt = _clock.GetUtcNow().AddSeconds(31) }, r with { PaidAt = null }, r with { OrderCreatedAt = null }, r with { CancelledAt = null } };
        foreach (var bad in invalid) await Check(await _client.PostAsJsonAsync(Route, bad), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT");
        await using var db = Db(); Assert.Empty(await db.OrderBuyerCancellationDispatches.ToListAsync()); Assert.False(await db.Notifications.AnyAsync(n => n.SourceEventId == r.SourceEventId));
    }

    [PostgresFact]
    public async Task InvalidIdentityReasonAndText_AreRejectedAtBoundary()
    {
        var r = Request(); var invalid = new[] { r with { SourceEventId = Guid.Empty }, r with { OrderId = null }, r with { ShopId = Guid.Empty }, r with { BuyerUserId = Guid.Empty }, r with { SellerUserId = null }, r with { SellerUserId = r.BuyerUserId }, r with { OrderNo = " " }, r with { OrderNo = new string('x', 101) }, r with { OrderNo = "bad\nnumber" }, r with { Reason = " " }, r with { Reason = new string('x', 501) }, r with { Reason = "bad\treason" }, r with { TraceId = new string('x', 101) }, r with { TraceId = "bad\ntrace" } };
        foreach (var bad in invalid) await Check(await _client.PostAsJsonAsync(Route, bad), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT");
    }

    [PostgresFact]
    public async Task StrictFieldsRolesAndSize_PreventMislabelledCancellationOrPrivatePayload()
    {
        var r = Request(); Role(null); await Check(await _client.PostAsJsonAsync(Route, r), HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "buyer", "seller", "staff" }) { Role(role); await Check(await _client.PostAsJsonAsync(Route, r), HttpStatusCode.Forbidden); }
        Role("admin"); await Check(await _client.PostAsJsonAsync(Route, r), HttpStatusCode.OK);
        Role("service"); await Check(await _client.PostAsync(Route, new StringContent(new string('x', 16385), Encoding.UTF8, "application/json")), HttpStatusCode.RequestEntityTooLarge);
        foreach (var field in new[] { "auctionId", "bidId", "resultId", "sellerAcceptDeadline", "paymentDeadline", "handedOverAt", "deliveredAt", "completedAt", "refundStatus", "refundAmount", "walletCreditId", "bankAccount", "balance", "stockReleased", "pointsDeducted", "buyerEmail", "phoneNumber", "address", "actionUrl", "password", "otp", "token" })
        {
            var node = JsonSerializer.SerializeToNode(Request(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject(); node[field] = "not-accepted";
            await Check(await _client.PostAsync(Route, new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT");
        }
    }

    [PostgresFact]
    public async Task HistoricalAndEqualTimes_AreAcceptedWithoutInferringDeadlineOrRefund()
    {
        var instant = _clock.GetUtcNow().AddDays(-90); var r = Request() with { OrderCreatedAt = instant, PaidAt = instant, CancelledAt = instant }; var accepted = await Accept(r);
        Role("buyer", r.BuyerUserId); var detail = (await _client.GetFromJsonAsync<InAppNotificationItemResponse>(Route + "/" + accepted.Buyer.NotificationId))!;
        using var p = JsonDocument.Parse(detail.Payload!); Assert.Equal(instant.ToUniversalTime().Ticks / 10, p.RootElement.GetProperty("cancelledAt").GetDateTimeOffset().Ticks / 10); Assert.False(p.RootElement.TryGetProperty("paymentDeadline", out _)); Assert.False(detail.IsRead);
        Assert.False((await Accept(Request() with { CancelledAt = _clock.GetUtcNow().AddSeconds(30) })).IsReplay);
    }

    [PostgresFact]
    public async Task CanonicalReplay_KeepsOriginalFanoutAndDoesNotReloadTemplates()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); var first = await Accept(r); await Device(r.BuyerUserId.Value);
        await using (var db = Db()) await db.NotificationTemplates.Where(t => t.NotificationType == NotificationType.OrderBuyerCancelled).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, false));
        var replay = await Accept(r with { OrderKind = "regular", PreviousOrderStatus = "paid", OrderStatus = "cancelled", CancelledBy = "buyer", HandoverStatus = "nothandedover", OrderNo = " " + r.OrderNo + " ", Reason = " " + r.Reason + " ", OrderCreatedAt = r.OrderCreatedAt!.Value.ToOffset(TimeSpan.FromHours(7)), PaidAt = r.PaidAt!.Value.ToOffset(TimeSpan.FromHours(7)), CancelledAt = r.CancelledAt!.Value.ToOffset(TimeSpan.FromHours(7)), TraceId = "new-trace" });
        Assert.True(replay.IsReplay); Assert.Equal(first.Buyer.NotificationId, replay.Buyer.NotificationId); Assert.Equal(first.Seller.NotificationId, replay.Seller.NotificationId); Assert.Equal(2, replay.Buyer.Deliveries.Count); Assert.True(await Process()); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task ChangedCanonicalFieldsAndHiddenOrderAlias_ConflictWithoutOverwriting()
    {
        var r = Request(); var accepted = await Accept(r); var changed = new[] { r with { BuyerUserId = Guid.NewGuid() }, r with { SellerUserId = Guid.NewGuid() }, r with { ShopId = Guid.NewGuid() }, r with { OrderId = Guid.NewGuid() }, r with { PreviousOrderStatus = "Shipping" }, r with { Reason = "Changed" }, r with { OrderNo = "Changed" }, r with { OrderCreatedAt = r.OrderCreatedAt!.Value.AddSeconds(-1) }, r with { PaidAt = r.PaidAt!.Value.AddSeconds(1) }, r with { CancelledAt = r.CancelledAt!.Value.AddSeconds(1) } };
        foreach (var bad in changed) await Check(await _client.PostAsJsonAsync(Route, bad), HttpStatusCode.Conflict, "ORDER_BUYER_CANCELLATION_CONFLICT");
        await using (var db = Db()) { await db.Notifications.Where(n => n.SourceEventId == r.SourceEventId).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDeleted, true)); await db.OrderBuyerCancellationDispatches.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); }
        await Check(await _client.PostAsJsonAsync(Route, r with { SourceEventId = Guid.NewGuid() }), HttpStatusCode.Conflict, "ORDER_BUYER_CANCELLATION_ALREADY_NOTIFIED");
        var replay = await Accept(r); Assert.True(replay.IsReplay); Assert.Equal(accepted.Buyer.NotificationId, replay.Buyer.NotificationId); Assert.False(replay.Buyer.InboxVisible);
    }

    [PostgresFact]
    public async Task FreshRecreatedOrder_CanNotifySameOwnersWithoutOverwritingHistory()
    {
        var r = Request(); var first = await Accept(r); var fresh = r with { SourceEventId = Guid.NewGuid(), OrderId = Guid.NewGuid() }; var next = await Accept(fresh);
        Assert.NotEqual(first.Buyer.NotificationId, next.Buyer.NotificationId); Assert.Equal(2, (await Inbox(r.BuyerUserId!.Value)).Items.Count); Assert.Equal(2, (await Inbox(r.SellerUserId!.Value)).Items.Count);
    }

    [PostgresFact]
    public async Task ConfirmedWalletRefund_IsSeparateItemAndNeverRewritesCancellationHistory()
    {
        var r = Request(); var cancelled = await Accept(r); await using (var db = Db())
        {
            await new Notification.Application.Services.OrderRefunds.OrderRefundService(db, _clock).CreateAsync(new()
            {
                SourceEventId = Guid.NewGuid(),
                RefundId = Guid.NewGuid(),
                WalletCreditTransactionId = Guid.NewGuid(),
                OrderId = r.OrderId,
                BuyerUserId = r.BuyerUserId,
                OrderKind = "Regular",
                OrderNo = r.OrderNo,
                RefundAmount = 123000m,
                Currency = "VND",
                RefundStatus = "Completed",
                WalletCreditStatus = "Completed",
                RefundDestination = "BuyerWallet",
                RequestedAt = r.CancelledAt,
                WalletCreditedAt = _clock.GetUtcNow(),
                CompletedAt = _clock.GetUtcNow()
            });
        }
        var inbox = await Inbox(r.BuyerUserId!.Value); Assert.Equal(2, inbox.Items.Count); Assert.Single(inbox.Items, n => n.NotificationType == "OrderRefunded");
        var original = Assert.Single(inbox.Items, n => n.Id == cancelled.Buyer.NotificationId); Assert.Contains("tiến trình hoàn tiền", original.Body); Assert.DoesNotContain("đã hoàn tiền", original.Body);
        Assert.Single((await Inbox(r.SellerUserId!.Value)).Items); Assert.True((await Accept(r)).IsReplay);
    }

    [PostgresFact]
    public async Task PreferencesAndSwagger_ExposeNewTypeAndPreservePreviousEnumValues()
    {
        Role("buyer"); var prefs = (await _client.GetFromJsonAsync<List<NotificationPreferenceResponse>>("/api/v1/notifications/preferences"))!; Assert.Equal(54, prefs.Count); Assert.Equal(18, prefs.Count(p => p.IsMandatory)); Assert.True(prefs.Single(p => p.NotificationType == "OrderBuyerCancelled" && p.ChannelCode == "Push").IsMandatory); Assert.False(prefs.Single(p => p.NotificationType == "OrderBuyerCancelled" && p.ChannelCode == "InApp").IsMandatory);
        var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route, swagger); Assert.Contains("previousOrderStatus", swagger); Assert.Contains("handoverStatus", swagger); Assert.Equal(17, (int)NotificationType.OrderBuyerCancelled); Assert.Equal(16, (int)NotificationType.OrderPaymentExpired); Assert.Equal(29, (int)AuditAction.OrderBuyerCancellationAccepted);
    }


    [PostgresFact]
    public async Task NoDevices_StillCreatesTwoUnreadInboxItemsAndSkippedPushMarkers()
    {
        var r = Request(); var result = await Accept(r);
        Assert.Equal("Completed", result.Buyer.NotificationStatus); Assert.True(result.Buyer.InboxVisible);
        Assert.Contains(result.Buyer.Deliveries, d => d.Channel == "Push" && d.ErrorCode == "PUSH_NO_ACTIVE_DEVICES");
        Assert.Equal(1, (await Inbox(r.BuyerUserId!.Value, true)).Pagination.TotalCount); Assert.Equal(1, (await Inbox(r.SellerUserId!.Value, true)).Pagination.TotalCount);
        Assert.False(await Process());
    }


    [PostgresFact]
    public async Task PushFanout_UsesSameLogicalId_AndDoesNotMultiplyBellItems()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value, 2); await Device(r.SellerUserId!.Value, 3); await Device(Guid.NewGuid());
        var accepted = await Accept(r); for (var i = 0; i < 5; i++) Assert.True(await Process()); Assert.False(await Process());
        Assert.Equal(5, _push.Messages.Count); Assert.All(_push.Messages, m => Assert.True(m.NotificationId == accepted.Buyer.NotificationId || m.NotificationId == accepted.Seller.NotificationId));
        Assert.Equal(2, _push.Messages.Count(m => m.UserId == r.BuyerUserId)); Assert.Equal(3, _push.Messages.Count(m => m.UserId == r.SellerUserId));
        Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); Assert.Single((await Inbox(r.SellerUserId.Value)).Items);
        var replay = await Accept(r); Assert.Equal("Completed", replay.Buyer.NotificationStatus); Assert.Equal("Completed", replay.Seller.NotificationStatus);
    }


    [PostgresFact]
    public async Task BuyerPushFailure_DoesNotLoseEitherInboxOrSellerPush()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await Device(r.SellerUserId!.Value);
        _push.Result = m => m.UserId == r.BuyerUserId ? PushDeliveryResult.Failure("FCM_AUTH_FAILED", "Test permanent failure.") : PushDeliveryResult.Sent("seller-ok");
        await Accept(r); await Process(); await Process(); var replay = await Accept(r);
        Assert.Equal("Partial", replay.Buyer.NotificationStatus); Assert.Equal("Completed", replay.Seller.NotificationStatus);
        Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); Assert.Single((await Inbox(r.SellerUserId.Value)).Items);
    }


    [PostgresFact]
    public async Task DisabledPush_PreservesInboxAndNeverRequeuesOnReplayAfterEnable()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await using (var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled, true));
        var result = await Accept(r); Assert.Contains(result.Buyer.Deliveries, d => d.Channel == "Push" && d.Status == "Skipped");
        Assert.Single((await Inbox(r.BuyerUserId.Value)).Items);
        await using (var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled, false));
        Assert.True((await Accept(r)).IsReplay); Assert.False(await Process());
    }


    [PostgresFact]
    public async Task DisabledInApp_PushStillRuns_AndBellReadActionsCannotResurrectItem()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await Device(r.SellerUserId!.Value);
        await using (var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled, true));
        var accepted = await Accept(r); Assert.False(accepted.Buyer.InboxVisible); Assert.Empty((await Inbox(r.BuyerUserId.Value)).Items);
        await Check(await _client.GetAsync(Route + "/" + accepted.Buyer.NotificationId), HttpStatusCode.OK);
        await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + accepted.Buyer.NotificationId + "/read-states", new { isRead = false }), HttpStatusCode.NotFound, "NOTIFICATION_NOT_FOUND");
        await Process(); await Process(); Assert.Equal(2, _push.Messages.Count); Assert.Empty((await Inbox(r.BuyerUserId.Value, true)).Items);
        await using var dbCheck = Db(); var row = await dbCheck.Notifications.SingleAsync(n => n.Id == accepted.Buyer.NotificationId);
        Assert.False(row.IsInboxVisible); Assert.False(row.IsDeleted); Assert.True(row.IsRead);
    }


    [PostgresFact]
    public async Task BothChannelsDisabled_RecordsSuppressionWithoutVisibleInboxOrProviderCall()
    {
        var r = Request(); await using (var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push || c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled, true));
        var accepted = await Accept(r); Assert.False(accepted.Buyer.InboxVisible); Assert.All(accepted.Buyer.Deliveries, d => Assert.Equal("Skipped", d.Status));
        Assert.Empty((await Inbox(r.BuyerUserId!.Value)).Items); Assert.False(await Process());
        await Check(await _client.GetAsync(Route + "/" + accepted.Buyer.NotificationId), HttpStatusCode.NotFound, "NOTIFICATION_NOT_FOUND");
    }


    [PostgresFact]
    public async Task DeviceOverflow_SkipsOnlyAffectedPushBranch_AndKeepsBothInboxes()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value, 101); await Device(r.SellerUserId!.Value);
        var accepted = await Accept(r); Assert.Contains(accepted.Buyer.Deliveries, d => d.ErrorCode == "PUSH_DEVICE_LIMIT");
        Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); Assert.Single((await Inbox(r.SellerUserId.Value)).Items);
        Assert.True(await Process()); Assert.False(await Process()); Assert.Equal(r.SellerUserId, Assert.Single(_push.Messages).UserId);
    }


    [PostgresFact]
    public async Task OwnerGetAndReadStates_ProtectRolesBadgeAndIndependentBranches()
    {
        var r = Request(); var result = await Accept(r); Role(null);
        await Check(await _client.GetAsync(Route + "/" + result.Buyer.NotificationId), HttpStatusCode.Unauthorized);
        Role("buyer", r.BuyerUserId); await Check(await _client.GetAsync(Route + "/" + result.Seller.NotificationId), HttpStatusCode.NotFound, "NOTIFICATION_NOT_FOUND");
        var detail = await _client.GetAsync(Route + "/" + result.Buyer.NotificationId); await Check(detail, HttpStatusCode.OK); Assert.True(detail.Headers.CacheControl!.NoStore);
        await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + result.Buyer.NotificationId + "/read-states", new { isRead = true }), HttpStatusCode.OK);
        Assert.Empty((await Inbox(r.BuyerUserId!.Value, true)).Items); Assert.Single((await Inbox(r.SellerUserId!.Value, true)).Items);
        Role("buyer", r.BuyerUserId); await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + result.Buyer.NotificationId + "/read-states", new { isRead = false }), HttpStatusCode.OK);
        Assert.Single((await Inbox(r.BuyerUserId.Value, true)).Items);
        await using var db = Db(); Assert.All(await db.NotificationDeliveries.Where(d => d.NotificationId == result.Buyer.NotificationId && d.ChannelCode == ChannelType.InApp).ToListAsync(), d => Assert.Equal(DeliveryStatus.Sent, d.Status));
    }


    [PostgresFact]
    public async Task ConcurrentSameEvent_CreatesTwoItemsOnceAndReplaysSevenTimes()
    {
        var r = Request(); var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Accept(r))).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results, x => !x.IsReplay); Assert.Single(results.Select(x => x.Buyer.NotificationId).Distinct());
        await using var db = Db(); Assert.Equal(2, await db.Notifications.CountAsync(n => n.SourceEventId == r.SourceEventId)); Assert.Single(await db.OrderBuyerCancellationDispatches.ToListAsync());
    }


    [PostgresFact]
    public async Task ConcurrentOrderAliases_CreateOnePairAndConflictOtherEvent()
    {
        var r = Request(); var statuses = await Task.WhenAll(new[] { r, r with { SourceEventId = Guid.NewGuid() } }.Select(async x =>
        { try { await Accept(x); return 200; } catch (DomainException ex) { Assert.Equal("ORDER_BUYER_CANCELLATION_ALREADY_NOTIFIED", ex.ErrorCode); return ex.StatusCode; } }));
        Assert.Single(statuses, x => x == 200); Assert.Single(statuses, x => x == 409);
        await using var db = Db(); Assert.Single(await db.OrderBuyerCancellationDispatches.ToListAsync());
    }


    [PostgresFact]
    public async Task SameEventDifferentOrdersRace_OnePairWinsOtherConflicts()
    {
        var r = Request(); var statuses = await Task.WhenAll(new[] { r, r with { OrderId = Guid.NewGuid(), SellerUserId = Guid.NewGuid() } }.Select(async x =>
        { try { await Accept(x); return 200; } catch (DomainException ex) { Assert.Equal("ORDER_BUYER_CANCELLATION_CONFLICT", ex.ErrorCode); return ex.StatusCode; } }));
        Assert.Single(statuses, x => x == 200); Assert.Single(statuses, x => x == 409);
    }


    [PostgresFact]
    public async Task AuditWriteFailure_RollsBackBothBranchesAllDeliveriesAndReceipt()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value);
        await using (var db = Db(new FailAllSave())) await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(r));
        await using var verify = Db(); Assert.Empty(await verify.OrderBuyerCancellationDispatches.ToListAsync()); Assert.False(await verify.Notifications.AnyAsync(n => n.SourceEventId == r.SourceEventId));
        Assert.Empty(await verify.AuditLogs.Where(a => a.Action == AuditAction.OrderBuyerCancellationAccepted).ToListAsync()); Assert.False(await Process()); Assert.False((await Accept(r)).IsReplay);
    }


    [PostgresFact]
    public async Task SellerTemplateFailure_DoesNotCommitBuyerBranch()
    {
        var r = Request(); await using (var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "ORDER_BUYER_CANCELLED_SELLER_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate, "{{TotalAmount}}"));
        await Check(await _client.PostAsJsonAsync(Route, r), HttpStatusCode.BadRequest, "TEMPLATE_DATA_MISSING");
        await using var verify = Db(); Assert.False(await verify.Notifications.AnyAsync(n => n.SourceEventId == r.SourceEventId)); Assert.Empty(await verify.OrderBuyerCancellationDispatches.ToListAsync());
    }


    [PostgresFact]
    public async Task InvalidTemplatesAndMissingChannels_AcceptNeitherBranch()
    {
        var r = Request(); await using var db = Db();
        await db.NotificationTemplates.Where(t => t.Code == "ORDER_BUYER_CANCELLED_BUYER_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory, false));
        await Check(await _client.PostAsJsonAsync(Route, r), HttpStatusCode.Conflict, "ORDER_BUYER_CANCELLATION_TEMPLATE_INVALID");
        await db.NotificationTemplates.Where(t => t.Code == "ORDER_BUYER_CANCELLED_BUYER_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory, true).SetProperty(t => t.IsActive, false));
        await Check(await _client.PostAsJsonAsync(Route, r), HttpStatusCode.NotFound, "TEMPLATE_NOT_FOUND");
        await db.NotificationTemplates.Where(t => t.Code == "ORDER_BUYER_CANCELLED_BUYER_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, true));
        await db.NotificationChannels.Where(c => c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted, true));
        await Check(await _client.PostAsJsonAsync(Route, r), HttpStatusCode.Conflict, "CHANNEL_NOT_FOUND"); Assert.Empty(await db.OrderBuyerCancellationDispatches.ToListAsync());
    }


    [PostgresFact]
    public async Task TransientPushRetry_DoesNotResendSuccessfulDeviceOrCreateNewInbox()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await Device(r.SellerUserId!.Value); var failures = 0;
        _push.Result = m => m.UserId == r.BuyerUserId && failures++ == 0 ? PushDeliveryResult.Failure("FCM_UNAVAILABLE", "Test retry.", true) : PushDeliveryResult.Sent("ok");
        await Accept(r); await Process(); await Process(); Assert.False(await Process()); _clock.Advance(TimeSpan.FromSeconds(10)); Assert.True(await Process());
        Assert.Equal(3, _push.Messages.Count); Assert.Single(_push.Messages, m => m.UserId == r.SellerUserId); Assert.Single((await Inbox(r.BuyerUserId!.Value)).Items);
    }


    [PostgresFact]
    public async Task RevokedDevice_IsSkippedWithoutLosingInboxOrSendingToNewOwner()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await Accept(r);
        await using (var db = Db()) await db.DeviceTokens.Where(d => d.UserId == r.BuyerUserId).ExecuteUpdateAsync(s => s.SetProperty(d => d.UserId, Guid.NewGuid()));
        Assert.True(await Process()); Assert.Empty(_push.Messages); Assert.Single((await Inbox(r.BuyerUserId.Value)).Items);
    }


    [PostgresFact]
    public async Task LegacyRowsRemainVisibleAndSwaggerExposesNewRoutes()
    {
        var r = Request(); await using (var db = Db())
        {
            Assert.True((await db.Notifications.FirstAsync()).IsInboxVisible);
            db.Notifications.Add(new() { Id = Guid.NewGuid(), UserId = r.BuyerUserId!.Value, Title = "Legacy", Body = "Legacy", Status = NotificationStatus.Completed }); await db.SaveChangesAsync();
        }
        Assert.Single((await Inbox(r.BuyerUserId!.Value)).Items); var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route, swagger); Assert.Contains("{notificationId}", swagger);
    }


    [PostgresFact]
    public async Task GenericDispatchCollision_RollsBackWholeNewPairInsteadOfAdoptingExistingContent()
    {
        var r = Request(); await using (var db = Db()) await new DispatchService(db).CreateAsync(new()
        {
            SourceEventId = r.SourceEventId,
            UserId = r.BuyerUserId,
            TemplateCode = "ORDER_BUYER_CANCELLED_BUYER_PUSH",
            Data = new() { ["OrderNo"] = "Different", ["CancelledAtUtc"] = "Different", ["Reason"] = "Different" }
        });
        await Check(await _client.PostAsJsonAsync(Route, r), HttpStatusCode.Conflict, "ORDER_BUYER_CANCELLATION_CONFLICT");
        await using var verify = Db(); Assert.Empty(await verify.OrderBuyerCancellationDispatches.ToListAsync());
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
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => editor.NotificationTemplates.Where(t => t.Code == "ORDER_BUYER_CANCELLED_SELLER_PUSH")
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.NotificationType, NotificationType.Promotion)));
            Assert.Equal("55P03", Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        finally { gate.Release.TrySetResult(); }
        Assert.False((await accepting.WaitAsync(TimeSpan.FromSeconds(5))).IsReplay);
    }


    [PostgresFact]
    public async Task MandatoryPushAndOptionalInApp_RespectOwnChannelAndGlobalDisable()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await using (var db = Db()) { foreach (var c in new[] { ChannelType.Push, ChannelType.InApp }) db.NotificationPreferences.Add(new() { Id = Guid.NewGuid(), UserId = r.BuyerUserId.Value, NotificationType = NotificationType.OrderBuyerCancelled, ChannelCode = c, IsDisabled = true }); await db.SaveChangesAsync(); }
        var result = await Accept(r); Assert.False(result.Buyer.InboxVisible); Assert.Contains(result.Buyer.Deliveries, d => d.Channel == "Push" && d.Status == "Pending"); Assert.Contains(result.Buyer.Deliveries, d => d.Channel == "InApp" && d.ErrorCode == "PREFERENCE_DISABLED"); Assert.True(await Process()); Assert.Empty((await Inbox(r.BuyerUserId.Value)).Items);
        await using (var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled, true)); Assert.Contains((await Accept(Request())).Buyer.Deliveries, d => d.ErrorCode == "CHANNEL_DISABLED");
    }


    [PostgresFact]
    public async Task CorruptedOneBranchManifest_FailsReplayWithoutReplacementPair()
    {
        var r = Request(); var result = await Accept(r); await using (var db = Db()) { await db.NotificationLogs.Where(l => l.NotificationId == result.Seller.NotificationId).ExecuteDeleteAsync(); await db.NotificationDeliveries.Where(d => d.NotificationId == result.Seller.NotificationId).ExecuteDeleteAsync(); }
        await Check(await _client.PostAsJsonAsync(Route, r), HttpStatusCode.Conflict, "DELIVERY_STATE_INVALID"); await using var verify = Db(); Assert.Equal(2, await verify.Notifications.CountAsync(n => n.SourceEventId == r.SourceEventId));
    }


    [PostgresFact]
    public async Task SnapshotRetry_PreservesOriginalCancellationTextWithoutCreditedWalletClaim()
    {
        var r = Request(); await Device(r.SellerUserId!.Value); await Accept(r); _push.Result = _ => PushDeliveryResult.Failure("FCM_UNAVAILABLE", "Test transient failure.", true); Assert.True(await Process()); var original = Assert.Single(_push.Messages).Body;
        await using (var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "ORDER_BUYER_CANCELLED_SELLER_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate, "Changed later")); _clock.Advance(TimeSpan.FromSeconds(10)); _push.Result = _ => PushDeliveryResult.Sent("ok"); Assert.True(await Process()); Assert.Equal(original, _push.Messages.Last().Body); Assert.DoesNotContain("tiền đã vào ví", original);
    }

    private sealed class GateSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); return result; }
    }

    private async Task Device(Guid owner, int count = 1)
    {
        await using var db = Db();
        for (var i = 0; i < count; i++) db.DeviceTokens.Add(new() { Id = Guid.NewGuid(), UserId = owner, Token = "test-" + Guid.NewGuid().ToString("N"), IsActive = true, Platform = DevicePlatform.Web });
        await db.SaveChangesAsync();
    }
    private async Task<PagedData<InAppNotificationItemResponse>> Inbox(Guid user, bool unread = false)
    {
        Role("buyer", user);
        return (await _client.GetFromJsonAsync<PagedData<InAppNotificationItemResponse>>("/api/v1/notifications/me" + (unread ? "?isRead=false" : "")))!;
    }
    private sealed class FakePush : IPushDeliverySender
    {
        public List<PushDeliveryMessage> Messages { get; } = new();
        public Func<PushDeliveryMessage, PushDeliveryResult> Result { get; set; } = _ => PushDeliveryResult.Sent("test-provider-id");
        public Task<PushDeliveryResult> SendAsync(PushDeliveryMessage message, CancellationToken ct = default) { Messages.Add(message); return Task.FromResult(Result(message)); }
    }
    private sealed class FailAllSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
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
        public Task<EmailDeliveryResult> SendAsync(EmailDeliveryMessage message, CancellationToken ct = default)
        { Messages.Add(message); return Task.FromResult(Result); }
    }
    private sealed class FailSecondSave : SaveChangesInterceptor
    {
        private int _count;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (++_count == 2) throw new InvalidOperationException("Test audit failure.");
            return ValueTask.FromResult(result);
        }
    }
}
