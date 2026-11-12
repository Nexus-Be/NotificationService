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
using Notification.Application.Services.OrderPaymentTimeouts;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class OrderPaymentTimeoutApiIntegrationTests : IAsyncLifetime
{
    private const string Route = "/api/v1/notifications/order-payment-timeouts";
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
    private OrderPaymentTimeoutService Service(AppDbContext db) => new(db, _clock);
    private CreateOrderPaymentTimeoutRequest Request() => new()
    {
        SourceEventId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        BuyerUserId = Guid.NewGuid(),
        OrderNo = "ORDER-<b>DEMO</b>",
        OrderKind = "Regular",
        OrderStatus = "Cancelled",
        PaymentStatus = "Expired",
        OrderCreatedAt = _clock.GetUtcNow().AddHours(-2),
        PaymentDeadline = _clock.GetUtcNow().AddHours(-1),
        CancelledAt = _clock.GetUtcNow().AddMinutes(-1)
    };
    private async Task<OrderPaymentTimeoutResponse> Accept(CreateOrderPaymentTimeoutRequest request)
    { await using var db = Db(); return await Service(db).CreateAsync(request); }
    private async Task<bool> Process()
    { await using var db = Db(); return await new DeliveryService(db, _sender, new(), _clock, _push).ProcessNextAsync(); }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_order_payment_timeout_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"", admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(OrderPaymentTimeoutsController).Assembly)
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
    public async Task BuyerOnlyOneItem_RecordsSourceExpiryWithoutBankRefundOrPenaltyClaims()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value, 2); var other = Guid.NewGuid(); await Device(other); var response = await _client.PostAsJsonAsync(Route, r); await Check(response, HttpStatusCode.OK); Assert.True(response.Headers.CacheControl!.NoStore); var result = (await response.Content.ReadFromJsonAsync<OrderPaymentTimeoutResponse>())!; Assert.Equal(r.OrderId, result.OrderId); Assert.Equal(r.BuyerUserId, result.UserId); Assert.Equal(3, result.Deliveries.Count); Assert.Empty(_push.Messages);
        Assert.True(await Process()); Assert.True(await Process()); Assert.False(await Process()); Assert.Equal(2, _push.Messages.Count); Assert.All(_push.Messages, m => Assert.Equal(r.BuyerUserId, m.UserId)); Assert.Empty(_sender.Messages);
        var item = Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); Assert.Empty((await Inbox(other)).Items); Assert.Contains("quá hạn xác nhận thanh toán", item.Body); Assert.Contains("Hạn gốc", item.Body); Assert.DoesNotContain("chưa chuyển tiền", item.Body); Assert.DoesNotContain("thanh toán lại", item.Body); Assert.DoesNotContain("đã hoàn tiền", item.Body); Assert.DoesNotContain("đã nhả kho", item.Body); Assert.DoesNotContain("trừ uy tín", item.Body); Assert.DoesNotContain("phạt tiền", item.Body); Assert.DoesNotContain(r.BuyerUserId.ToString()!, item.Payload);
        using var p = JsonDocument.Parse(item.Payload!); Assert.Equal("OrderPaymentExpired", p.RootElement.GetProperty("type").GetString()); Assert.Equal("Buyer", p.RootElement.GetProperty("audience").GetString()); Assert.Equal("Regular", p.RootElement.GetProperty("orderKind").GetString()); Assert.Equal("Cancelled", p.RootElement.GetProperty("orderStatus").GetString()); Assert.Equal("Expired", p.RootElement.GetProperty("paymentStatus").GetString());
        await using var db = Db(); Assert.Single(await db.OrderPaymentTimeoutDispatches.ToListAsync()); Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditAction.OrderPaymentExpiryAccepted).ToListAsync()); Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task BeforeDeadlineWrongStatesKindsOrClock_RejectWithoutAcceptance()
    {
        var r = Request(); foreach (var invalid in new[] { r with { OrderKind = "Auction" }, r with { OrderStatus = "Paid" }, r with { OrderStatus = "PendingPayment" }, r with { OrderStatus = "Refunded" }, r with { PaymentStatus = "Paid" }, r with { PaymentStatus = "Pending" }, r with { PaymentStatus = "Refunded" }, r with { PaymentDeadline = _clock.GetUtcNow().AddMinutes(1), CancelledAt = _clock.GetUtcNow().AddMinutes(1) }, r with { PaymentDeadline = r.OrderCreatedAt }, r with { CancelledAt = r.PaymentDeadline!.Value.AddSeconds(-1) }, r with { CancelledAt = _clock.GetUtcNow().AddMinutes(1) }, r with { OrderCreatedAt = null }, r with { PaymentDeadline = null }, r with { CancelledAt = null } }) await Check(await _client.PostAsJsonAsync(Route, invalid), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT"); await using var db = Db(); Assert.Empty(await db.OrderPaymentTimeoutDispatches.ToListAsync()); Assert.False(await db.Notifications.AnyAsync(n => n.SourceEventId == r.SourceEventId));
    }

    [PostgresFact]
    public async Task InvalidIdsAndText_AreRejectedAtBoundary()
    {
        var r = Request(); foreach (var invalid in new[] { r with { SourceEventId = Guid.Empty }, r with { OrderId = null }, r with { BuyerUserId = Guid.Empty }, r with { OrderNo = " " }, r with { OrderNo = "bad\n" }, r with { TraceId = "\n" }, r with { OrderNo = new string('x', 101) }, r with { TraceId = new string('x', 101) } }) await Check(await _client.PostAsJsonAsync(Route, invalid), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT"); await using var db = Db(); Assert.Empty(await db.OrderPaymentTimeoutDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task UnknownAuctionSellerMoneyBankAndSecretFields_AreNotAccepted()
    {
        var r = Request(); foreach (var field in new[] { "auctionId", "resultId", "winningBidId", "sellerUserId", "sellerAcceptDeadline", "paidAt", "paidAmount", "refundCompleted", "stockReleased", "reputationPoints", "bankAccount", "email", "phone", "paymentUrl", "password", "otpCode", "token" }) { var b = JsonSerializer.SerializeToNode(r)!.AsObject(); b[field] = "private"; await Check(await _client.PostAsync(Route, new StringContent(b.ToJsonString(), Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest, "VAL_INVALID_INPUT"); }
        await using var db = Db(); Assert.Empty(await db.OrderPaymentTimeoutDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task ExactDeadlineAndHistoricalSourceTimes_DoNotDeriveDurationOrResetClock()
    {
        var now = _clock.GetUtcNow(); await Accept(Request() with { PaymentDeadline = now, CancelledAt = now }); var r = Request() with { OrderCreatedAt = now.AddDays(-5), PaymentDeadline = now.AddDays(-4), CancelledAt = now.AddDays(-3) }; await Accept(r); var item = Assert.Single((await Inbox(r.BuyerUserId!.Value)).Items); using var p = JsonDocument.Parse(item.Payload!); Assert.Equal(r.PaymentDeadline!.Value.UtcTicks / 10 * 10, p.RootElement.GetProperty("paymentDeadline").GetDateTimeOffset().UtcTicks); Assert.Equal(r.CancelledAt!.Value.UtcTicks / 10 * 10, p.RootElement.GetProperty("cancelledAt").GetDateTimeOffset().UtcTicks);
    }

    [PostgresFact]
    public async Task CanonicalReplay_KeepsOriginalFanoutAndDeadlineWithoutLoadingTemplates()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); var first = await Accept(r); await Device(r.BuyerUserId.Value); await using (var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "ORDER_PAYMENT_EXPIRED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, false));
        var replay = await Accept(r with { OrderKind = "regular", OrderStatus = "cancelled", PaymentStatus = "expired", OrderNo = " " + r.OrderNo + " ", OrderCreatedAt = r.OrderCreatedAt!.Value.ToOffset(TimeSpan.FromHours(7)), PaymentDeadline = r.PaymentDeadline!.Value.ToOffset(TimeSpan.FromHours(7)), CancelledAt = r.CancelledAt!.Value.ToOffset(TimeSpan.FromHours(7)), TraceId = "retry" }); Assert.True(replay.IsReplay); Assert.Equal(first.NotificationId, replay.NotificationId); Assert.Equal(first.Deliveries.Select(d => d.Id), replay.Deliveries.Select(d => d.Id));
    }

    [PostgresFact]
    public async Task ChangedBuyerOrderOrClock_ConflictsAndHiddenKeysRemainDeduplicated()
    {
        var r = Request(); var result = await Accept(r); foreach (var changed in new[] { r with { BuyerUserId = Guid.NewGuid() }, r with { OrderId = Guid.NewGuid() }, r with { OrderNo = "changed" }, r with { OrderCreatedAt = r.OrderCreatedAt!.Value.AddSeconds(-1) }, r with { PaymentDeadline = r.PaymentDeadline!.Value.AddMinutes(1) }, r with { CancelledAt = r.CancelledAt!.Value.AddSeconds(1) } }) { var e = await Assert.ThrowsAsync<DomainException>(() => Accept(changed)); Assert.Equal("ORDER_PAYMENT_TIMEOUT_CONFLICT", e.ErrorCode); }
        await using (var db = Db()) { await db.OrderPaymentTimeoutDispatches.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); await db.Notifications.Where(n => n.Id == result.NotificationId).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDeleted, true)); }
        Assert.True((await Accept(r)).IsReplay); var alias = Request() with { OrderId = r.OrderId }; var error = await Assert.ThrowsAsync<DomainException>(() => Accept(alias)); Assert.Equal("ORDER_PAYMENT_TIMEOUT_ALREADY_NOTIFIED", error.ErrorCode);
    }

    [PostgresFact]
    public async Task FreshRecreatedOrder_CanNotifySameBuyerWithoutOverwritingOldExpiry()
    {
        var r = Request(); var first = await Accept(r); var second = await Accept(Request() with { BuyerUserId = r.BuyerUserId, OrderNo = r.OrderNo }); Assert.NotEqual(first.NotificationId, second.NotificationId); Assert.Equal(2, (await Inbox(r.BuyerUserId!.Value)).Items.Count); await using var db = Db(); Assert.Equal(2, await db.OrderPaymentTimeoutDispatches.CountAsync());
    }

    [PostgresFact]
    public async Task ConcurrentOrderAliases_AcceptOneExpiryOnly()
    {
        var r = Request(); var statuses = await Task.WhenAll(new[] { r, r with { SourceEventId = Guid.NewGuid() } }.Select(async x => { try { await Accept(x); return 200; } catch (DomainException e) { Assert.Equal("ORDER_PAYMENT_TIMEOUT_ALREADY_NOTIFIED", e.ErrorCode); return e.StatusCode; } })); Assert.Single(statuses, x => x == 200); Assert.Single(statuses, x => x == 409); await using var db = Db(); Assert.Single(await db.OrderPaymentTimeoutDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task MandatoryPushOptionalInboxAndGlobalDisable_UseCorrectPolicy()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await Preference(r.BuyerUserId.Value, ChannelType.Push); await Preference(r.BuyerUserId.Value, ChannelType.InApp); var result = await Accept(r); Assert.False(result.InboxVisible); Assert.Contains(result.Deliveries, d => d.Channel == "Push" && d.Status == "Pending"); Assert.Contains(result.Deliveries, d => d.Channel == "InApp" && d.ErrorCode == "PREFERENCE_DISABLED"); Assert.True(await Process()); Assert.Empty((await Inbox(r.BuyerUserId.Value)).Items);
        await using (var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled, true)); Assert.Contains((await Accept(Request())).Deliveries, d => d.ErrorCode == "CHANNEL_DISABLED");
    }

    [PostgresFact]
    public async Task TemplateConfigurationRenderAndMissingChannel_DoNotAcceptExpiry()
    {
        await using (var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "ORDER_PAYMENT_EXPIRED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, false)); await Check(await _client.PostAsJsonAsync(Route, Request()), HttpStatusCode.NotFound, "TEMPLATE_NOT_FOUND");
        await using (var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "ORDER_PAYMENT_EXPIRED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, true).SetProperty(t => t.IsMandatory, false)); await Check(await _client.PostAsJsonAsync(Route, Request()), HttpStatusCode.Conflict, "ORDER_PAYMENT_TIMEOUT_TEMPLATE_INVALID");
        await using (var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "ORDER_PAYMENT_EXPIRED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.IsMandatory, true).SetProperty(t => t.BodyTemplate, "{{PaidAmount}}")); await Check(await _client.PostAsJsonAsync(Route, Request()), HttpStatusCode.BadRequest, "TEMPLATE_DATA_MISSING");
        await using (var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "ORDER_PAYMENT_EXPIRED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate, "{{OrderNo")); await Check(await _client.PostAsJsonAsync(Route, Request()), HttpStatusCode.Conflict, "TEMPLATE_INVALID");
        await using (var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted, true)); await Check(await _client.PostAsJsonAsync(Route, Request()), HttpStatusCode.Conflict, "CHANNEL_NOT_FOUND"); await using var verify = Db(); Assert.Empty(await verify.OrderPaymentTimeoutDispatches.ToListAsync());
    }

    [PostgresFact]
    public async Task GenericCollisionAndCorruptedManifest_AreNotAdoptedOrRecreated()
    {
        var r = Request(); await using (var db = Db()) await new DispatchService(db).CreateAsync(new() { SourceEventId = r.SourceEventId, UserId = r.BuyerUserId, TemplateCode = "ORDER_PAYMENT_EXPIRED_PUSH", Data = new() { ["OrderNo"] = "Other", ["PaymentDeadlineUtc"] = "Other", ["CancelledAtUtc"] = "Other" } }); await Check(await _client.PostAsJsonAsync(Route, r), HttpStatusCode.Conflict, "ORDER_PAYMENT_TIMEOUT_CONFLICT");
        var other = Request(); var accepted = await Accept(other); await using (var db = Db()) { await db.NotificationLogs.Where(l => l.NotificationId == accepted.NotificationId).ExecuteDeleteAsync(); await db.NotificationDeliveries.Where(d => d.NotificationId == accepted.NotificationId).ExecuteDeleteAsync(); }
        await Check(await _client.PostAsJsonAsync(Route, other), HttpStatusCode.Conflict, "DELIVERY_STATE_INVALID");
    }

    [PostgresFact]
    public async Task RetryAfterTemplateEdit_PreservesOriginalExpiryAndNeverAsksPaymentAgain()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await Accept(r); _push.Result = _ => PushDeliveryResult.Failure("FCM_UNAVAILABLE", "Test transient failure.", true); Assert.True(await Process()); var original = Assert.Single(_push.Messages).Body; await using (var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "ORDER_PAYMENT_EXPIRED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate, "Changed later")); _clock.Advance(TimeSpan.FromSeconds(10)); _push.Result = _ => PushDeliveryResult.Sent("ok"); Assert.True(await Process()); Assert.Equal(original, _push.Messages.Last().Body); Assert.DoesNotContain("thanh toán lại", original); Assert.DoesNotContain("chưa chuyển tiền", original);
    }

    [PostgresFact]
    public async Task SwaggerPreferencesAndEnums_ExposeRegularExpiryAsSeparateType()
    {
        var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Route, swagger); Assert.Contains("paymentDeadline", swagger); Assert.Contains("paymentStatus", swagger); Role("buyer"); var prefs = (await _client.GetFromJsonAsync<List<NotificationPreferenceResponse>>("/api/v1/notifications/preferences"))!; Assert.Equal(54, prefs.Count); Assert.Equal(18, prefs.Count(p => p.IsMandatory)); Assert.True(prefs.Single(p => p.NotificationType == "OrderPaymentExpired" && p.ChannelCode == "Push").IsMandatory); Assert.False(prefs.Single(p => p.NotificationType == "OrderPaymentExpired" && p.ChannelCode == "InApp").IsMandatory); Assert.Equal(16, (int)NotificationType.OrderPaymentExpired); Assert.Equal(15, (int)NotificationType.OrderSellerSettled); Assert.Equal(28, (int)AuditAction.OrderPaymentExpiryAccepted);
    }

    [PostgresFact]
    public async Task ConcurrentSameEvent_AcceptsOnce_ChangedRecipientConflicts()
    {
        var r = Request(); var replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Accept(r))); Assert.Single(replies, x => !x.IsReplay); Assert.Single(replies.Select(x => x.NotificationId).Distinct());
        var other = Request(); var statuses = await Task.WhenAll(new[] { other, other with { BuyerUserId = Guid.NewGuid() } }.Select(async x => { try { await Accept(x); return 200; } catch (DomainException ex) { Assert.Equal("ORDER_PAYMENT_TIMEOUT_CONFLICT", ex.ErrorCode); return ex.StatusCode; } })); Assert.Single(statuses, x => x == 200); Assert.Single(statuses, x => x == 409);
    }

    [PostgresFact]
    public async Task BothChannelsDisabled_HidesItemAndReplayNeverRequeues()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value);
        await using (var db = Db()) await db.NotificationChannels.Where(c => c.Code == ChannelType.Push || c.Code == ChannelType.InApp).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled, true));
        var result = await Accept(r); Assert.False(result.InboxVisible); Assert.All(result.Deliveries, d => Assert.Equal("Skipped", d.Status)); Assert.Empty((await Inbox(r.BuyerUserId.Value)).Items);
        await using (var db = Db()) await db.NotificationChannels.ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDisabled, false));
        Assert.All((await Accept(r)).Deliveries, d => Assert.Equal("Skipped", d.Status)); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task OwnerDetailReadBadgeAndForeignProtection_UsesOneLogicalItem()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); var accepted = await Accept(r); Role("buyer", r.BuyerUserId);
        var path = Route + "/" + accepted.NotificationId; await Check(await _client.GetAsync(path), HttpStatusCode.OK); Assert.Single((await Inbox(r.BuyerUserId.Value, true)).Items);
        await Check(await _client.PutAsJsonAsync("/api/v1/notifications/" + accepted.NotificationId + "/read-states", new { isRead = true }), HttpStatusCode.OK);
        Assert.Empty((await Inbox(r.BuyerUserId.Value, true)).Items); Role("buyer", Guid.NewGuid()); await Check(await _client.GetAsync(path), HttpStatusCode.NotFound, "NOTIFICATION_NOT_FOUND");
        Role(null); await Check(await _client.GetAsync(path), HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        Role("buyer", r.BuyerUserId); await using (var db = Db()) await db.Notifications.Where(n => n.Id == accepted.NotificationId).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDeleted, true)); await Check(await _client.GetAsync(path), HttpStatusCode.NotFound, "NOTIFICATION_NOT_FOUND");
    }

    [PostgresFact]
    public async Task PushOnlyDetailIsOwned_AndNotInBell()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); await Preference(r.BuyerUserId.Value, ChannelType.InApp); var result = await Accept(r);
        Role("buyer", r.BuyerUserId); await Check(await _client.GetAsync(Route + "/" + result.NotificationId), HttpStatusCode.OK); Assert.Empty((await Inbox(r.BuyerUserId.Value, true)).Items);
        Role("buyer", Guid.NewGuid()); await Check(await _client.GetAsync(Route + "/" + result.NotificationId), HttpStatusCode.NotFound, "NOTIFICATION_NOT_FOUND");
    }

    [PostgresFact]
    public async Task NoDevicesOrOverflow_SkipOnlyPushAndKeepInbox()
    {
        var a = Request(); Assert.Contains((await Accept(a)).Deliveries, d => d.ErrorCode == "PUSH_NO_ACTIVE_DEVICES");
        var b = Request(); await Device(b.BuyerUserId!.Value, 101); Assert.Contains((await Accept(b)).Deliveries, d => d.ErrorCode == "PUSH_DEVICE_LIMIT");
        Assert.Single((await Inbox(a.BuyerUserId!.Value)).Items); Assert.Single((await Inbox(b.BuyerUserId.Value)).Items); Assert.False(await Process());
    }

    [PostgresFact]
    public async Task RevokedDeviceAfterAcceptance_DoesNotSendToPreviousOwner()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); var result = await Accept(r);
        await using (var db = Db()) await db.DeviceTokens.Where(d => d.UserId == r.BuyerUserId).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, false));
        Assert.True(await Process()); Assert.Empty(_push.Messages); Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); Assert.DoesNotContain((await Accept(r)).Deliveries, d => d.Channel == "Push" && d.Status == "Sent");
    }

    [PostgresFact]
    public async Task PersistenceFailure_RollsBackReceiptGroupAndAudit_ThenRetryAccepts()
    {
        var r = Request(); await using (var db = Db(new FailAllSave())) await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(r));
        await using (var db = Db()) { Assert.Empty(await db.OrderPaymentTimeoutDispatches.ToListAsync()); Assert.Empty(await db.Notifications.Where(n => n.SourceEventId == r.SourceEventId).ToListAsync()); Assert.Empty(await db.AuditLogs.Where(a => a.Action == AuditAction.OrderPaymentExpiryAccepted).ToListAsync()); }
        Assert.False((await Accept(r)).IsReplay);
    }

    [PostgresFact]
    public async Task TemplateSharedLock_PreventsMetadataEditBeforeAcceptanceCommit()
    {
        var r = Request(); var gate = new GateSave(); await using var acceptingDb = Db(gate); var accepting = Service(acceptingDb).CreateAsync(r); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var editor = Db(); await using var tx = await editor.Database.BeginTransactionAsync(); await editor.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => editor.NotificationTemplates.Where(t => t.Code == "ORDER_PAYMENT_EXPIRED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.NotificationType, NotificationType.Promotion))); Assert.Equal("55P03", Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        finally { gate.Release.TrySetResult(); }
        Assert.False((await accepting.WaitAsync(TimeSpan.FromSeconds(5))).IsReplay);
    }

    [PostgresFact]
    public async Task RoleAndBodyLimit_OnlyServiceOrAdminCanAccept()
    {
        var r = Request(); Role(null); await Check(await _client.PostAsJsonAsync(Route, r), HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        foreach (var role in new[] { "buyer", "seller", "staff" }) { Role(role); await Check(await _client.PostAsJsonAsync(Route, r), HttpStatusCode.Forbidden, "FORBIDDEN"); }
        Role("service"); await Check(await _client.PostAsJsonAsync(Route, r with { OrderNo = new string('x', 17000) }), HttpStatusCode.RequestEntityTooLarge, "REQUEST_BODY_TOO_LARGE"); Role("admin"); await Check(await _client.PostAsJsonAsync(Route, r), HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task WorkerFailureRetry_KeepsInboxAndOriginalPlainTextSnapshot()
    {
        var r = Request(); await Device(r.BuyerUserId!.Value); _push.Result = _ => PushDeliveryResult.Failure("FCM_UNAVAILABLE", "Test transient failure.", true); await Accept(r); Assert.True(await Process()); var original = Assert.Single(_push.Messages).Body;
        await using (var db = Db()) await db.NotificationTemplates.Where(t => t.Code == "ORDER_PAYMENT_EXPIRED_PUSH").ExecuteUpdateAsync(s => s.SetProperty(t => t.BodyTemplate, "Changed later"));
        _clock.Advance(TimeSpan.FromSeconds(10)); _push.Result = _ => PushDeliveryResult.Sent("ok"); Assert.True(await Process()); Assert.Equal(original, _push.Messages.Last().Body); Assert.Single((await Inbox(r.BuyerUserId.Value)).Items); Assert.Equal("Completed", (await Accept(r)).NotificationStatus);
        var other = Request(); await Device(other.BuyerUserId!.Value); await Accept(other); _push.Result = _ => PushDeliveryResult.Failure("FCM_AUTH_FAILED", "Test permanent failure."); Assert.True(await Process()); Assert.Equal("Partial", (await Accept(other)).NotificationStatus); Assert.Single((await Inbox(other.BuyerUserId.Value)).Items);
    }

    private sealed class GateSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); return result; }
    }

    private async Task Preference(Guid owner, ChannelType channel, bool disabled = true)
    {
        await using var db = Db();
        db.NotificationPreferences.Add(new() { Id = Guid.NewGuid(), UserId = owner, NotificationType = NotificationType.OrderPaymentExpired, ChannelCode = channel, IsDisabled = disabled });
        await db.SaveChangesAsync();
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
}
