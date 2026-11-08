namespace Notification.Tests;

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Notification.Application.Interfaces;
using Notification.Application.Services.Deliveries;
using Notification.Application.Services.Dispatches;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class PostgresPushTheoryAttribute : TheoryAttribute
{
    public PostgresPushTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB")))
            Skip = "Requires isolated PostgreSQL via NOTIFICATION_TEST_DB.";
    }
}

public sealed class PostgresPushDeliveryTests : IAsyncLifetime
{
    private string? _admin, _database, _connection;
    private readonly Clock _clock = new();
    private readonly DeliveryWorkerSettings _settings = new();

    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_push_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"", admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is null || _admin is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await new NpgsqlCommand("DROP DATABASE \"" + _database + "\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }

    private AppDbContext Db(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new(builder.Options);
    }

    private async Task<List<DeviceToken>> Devices(Guid user, int count)
    {
        await using var db = Db();
        var devices = Enumerable.Range(0, count).Select(i => new DeviceToken
        {
            Id = Guid.NewGuid(), UserId = user, Token = "fake-private-token-" + Guid.NewGuid(), IsActive = true,
            Platform = (DevicePlatform)(i % 3)
        }).ToList();
        db.DeviceTokens.AddRange(devices);
        await db.SaveChangesAsync();
        return devices;
    }

    private static CreateDispatchRequest Request(Guid user) => DispatchServiceTests.Request("ORDER_PAID_PUSH") with { UserId = user, Destination = null };
    private async Task<DispatchResponse> Dispatch(CreateDispatchRequest request)
    {
        await using var db = Db();
        return await new DispatchService(db).CreateAsync(request);
    }
    private async Task<bool> Process(Push sender)
    {
        await using var db = Db();
        return await new DeliveryService(db, new NoEmail(), _settings, _clock, sender).ProcessNextAsync();
    }

    [PostgresFact]
    public async Task FanoutAndReplay_SnapshotOwnedDevicesAndDoNotExposeTokens()
    {
        var user = Guid.NewGuid();
        var devices = await Devices(user, 2);
        await Devices(Guid.NewGuid(), 1);
        var request = Request(user);
        var first = await Dispatch(request);
        Assert.Equal(2, first.Deliveries.Count);
        await Devices(user, 1);
        var replay = await Dispatch(request);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Deliveries.Select(d => d.DeliveryId), replay.Deliveries.Select(d => d.DeliveryId));
        await using var db = Db();
        var rows = await db.NotificationDeliveries.Where(d => d.NotificationId == first.NotificationId).ToListAsync();
        Assert.All(rows, row => Assert.Contains(devices, device => device.Id.ToString() == row.Destination));
        Assert.Equal(2, rows.Count);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task MandatoryPush_BypassesOptOut_WhileOptionalPushIsSuppressed()
    {
        var user = Guid.NewGuid();
        await Devices(user, 1);
        await using (var db = Db())
        {
            db.NotificationPreferences.AddRange(
                new NotificationPreference { Id = Guid.NewGuid(), UserId = user, NotificationType = NotificationType.OrderPaid, ChannelCode = ChannelType.Push, IsDisabled = true },
                new NotificationPreference { Id = Guid.NewGuid(), UserId = user, NotificationType = NotificationType.AuctionOutbid, ChannelCode = ChannelType.Push, IsDisabled = true });
            await db.SaveChangesAsync();
        }
        Assert.Equal("Pending", (await Dispatch(Request(user))).DeliveryStatus);
        var optional = await Dispatch(Request(user) with { TemplateCode = "AUCTION_OUTBID_PUSH", Data = new() { ["AuctionTitle"] = "Auction", ["BidAmount"] = "200" } });
        Assert.Equal("Skipped", optional.DeliveryStatus);
        await using var verify = Db();
        Assert.True((await verify.Notifications.IgnoreQueryFilters().SingleAsync(n => n.Id == optional.NotificationId)).IsDeleted);
    }

    [PostgresFact]
    public async Task NoDevicesAndDeviceCap_AreExplicitDecisions()
    {
        var user = Guid.NewGuid();
        var empty = await Dispatch(Request(user));
        Assert.Equal("Skipped", empty.DeliveryStatus);
        await using (var db = Db())
            Assert.Equal("PUSH_NO_ACTIVE_DEVICES", (await db.NotificationDeliveries.SingleAsync(d => d.Id == empty.DeliveryId)).ErrorCode);
        await Devices(user, 101);
        var request = Request(user);
        var error = await Assert.ThrowsAsync<DomainException>(() => Dispatch(request));
        Assert.Equal("TOO_MANY_DEVICES", error.ErrorCode);
        await using var verify = Db();
        Assert.Equal(0, await verify.Notifications.CountAsync(n => n.SourceEventId == request.SourceEventId));
    }

    [PostgresFact]
    public async Task UnregisteredToken_DeactivatesOnlyItsSnapshotAndWritesAudit()
    {
        var user = Guid.NewGuid();
        var device = Assert.Single(await Devices(user, 1));
        var dispatch = await Dispatch(Request(user));
        var sender = new Push { Handler = (_, _) => Task.FromResult(PushDeliveryResult.Failure("FCM_UNREGISTERED", "Token expired.", invalidateToken: true)) };
        await Process(sender);
        await using var verify = Db();
        Assert.False((await verify.DeviceTokens.SingleAsync(d => d.Id == device.Id)).IsActive);
        var delivery = await verify.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId);
        Assert.Equal(DeliveryStatus.Failed, delivery.Status);
        var audit = await verify.AuditLogs.SingleAsync(a => a.EntityId == device.Id.ToString());
        Assert.Equal(AuditAction.DeviceRevoke, audit.Action);
        Assert.Contains("FCM_UNREGISTERED", audit.NewValues);
        var logs = await verify.NotificationLogs.Where(l => l.NotificationId == dispatch.NotificationId).ToListAsync();
        Assert.All(logs, l => Assert.DoesNotContain(device.Token, (l.Metadata ?? "") + l.Message));
    }

    [PostgresPushTheory]
    [InlineData("owner")]
    [InlineData("token")]
    [InlineData("renewed")]
    public async Task LateUnregisteredResult_DoesNotDisableChangedDevice(string change)
    {
        var user = Guid.NewGuid();
        var device = Assert.Single(await Devices(user, 1));
        await Dispatch(Request(user));
        var sender = new Push { Handler = async (_, _) =>
        {
            await using var changed = Db();
            var row = await changed.DeviceTokens.SingleAsync(d => d.Id == device.Id);
            if (change == "owner") row.UserId = Guid.NewGuid();
            if (change == "token") row.Token = "new-registration-token";
            row.UpdatedAt = _clock.GetUtcNow().AddMinutes(1);
            await changed.SaveChangesAsync();
            return PushDeliveryResult.Failure("FCM_UNREGISTERED", "Old token expired.", invalidateToken: true);
        } };
        await Process(sender);
        await using var verify = Db();
        Assert.True((await verify.DeviceTokens.SingleAsync(d => d.Id == device.Id)).IsActive);
        Assert.Equal(0, await verify.AuditLogs.CountAsync(a => a.EntityId == device.Id.ToString()));
    }

    [PostgresPushTheory]
    [InlineData("revoked")]
    [InlineData("reassigned")]
    [InlineData("deleted")]
    public async Task DeviceChangedBeforeSend_IsSkippedWithoutProviderCall(string change)
    {
        var user = Guid.NewGuid();
        var device = Assert.Single(await Devices(user, 1));
        var dispatch = await Dispatch(Request(user));
        await using (var db = Db())
        {
            var row = await db.DeviceTokens.SingleAsync(d => d.Id == device.Id);
            if (change == "revoked") row.IsActive = false;
            if (change == "reassigned") row.UserId = Guid.NewGuid();
            if (change == "deleted") row.IsDeleted = true;
            await db.SaveChangesAsync();
        }
        var sender = new Push();
        await Process(sender);
        Assert.Equal(0, sender.Calls);
        await using var verify = Db();
        Assert.Equal(DeliveryStatus.Skipped, (await verify.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId)).Status);
        Assert.True((await verify.Notifications.SingleAsync(n => n.Id == dispatch.NotificationId)).IsDeleted);
    }

    [PostgresFact]
    public async Task PerDeviceRetry_RespectsProviderDelayWithoutResendingSuccessfulDevices()
    {
        var user = Guid.NewGuid();
        var devices = await Devices(user, 2);
        var request = Request(user);
        var dispatch = await Dispatch(request);
        var tokenCalls = new ConcurrentDictionary<string,int>();
        var sender = new Push { Handler = (message, _) =>
        {
            var attempt = tokenCalls.AddOrUpdate(message.Token, 1, (_, count) => count + 1);
            return Task.FromResult(message.Token == devices[0].Token && attempt == 1 ?
                PushDeliveryResult.Failure("FCM_QUOTA_EXCEEDED", "Quota exceeded.", true, retryAfter: TimeSpan.FromSeconds(120)) : PushDeliveryResult.Sent("projects/test/messages/sent"));
        } };
        Assert.True(await Process(sender));
        Assert.True(await Process(sender));
        Assert.False(await Process(sender));
        _clock.Advance(119);
        Assert.False(await Process(sender));
        _clock.Advance(1);
        Assert.True(await Process(sender));
        Assert.Equal(2, tokenCalls[devices[0].Token]);
        Assert.Equal(1, tokenCalls[devices[1].Token]);
        var replay = await Dispatch(request);
        Assert.All(replay.Deliveries, d => Assert.Equal("Sent", d.Status));
        Assert.Equal("Completed", replay.NotificationStatus);
    }

    [PostgresFact]
    public async Task EightConcurrentDispatches_CreateOneFanoutManifest()
    {
        var user = Guid.NewGuid();
        await Devices(user, 2);
        var request = Request(user);
        var gate = new SaveGate(8);
        var results = await Task.WhenAll(Enumerable.Range(0,8).Select(async _ =>
        {
            await using var db = Db(gate);
            return await new DispatchService(db).CreateAsync(request);
        })).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results.Select(r => r.NotificationId).Distinct());
        Assert.Single(results, r => !r.IsReplay);
        Assert.All(results, r => Assert.Equal(2, r.Deliveries.Count));
        await using var verify = Db();
        Assert.Equal(2, await verify.NotificationDeliveries.CountAsync(d => d.NotificationId == results[0].NotificationId));
        Assert.Equal(2, await verify.NotificationLogs.CountAsync(l => l.NotificationId == results[0].NotificationId));
    }

    [PostgresFact]
    public async Task EightWorkers_OnlyOneSendsThePushDelivery()
    {
        var user = Guid.NewGuid();
        await Devices(user, 1);
        await Dispatch(Request(user));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new Push { Handler = async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); return PushDeliveryResult.Sent("accepted"); } };
        var tasks = Enumerable.Range(0,8).Select(_ => Process(sender)).ToArray();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var remaining = tasks.ToList();
            while (remaining.Count > 1)
            {
                var finished = await Task.WhenAny(remaining).WaitAsync(TimeSpan.FromSeconds(10));
                remaining.Remove(finished);
                await finished;
            }
            Assert.Equal(1, sender.Calls);
        }
        finally { release.TrySetResult(); await Task.WhenAll(tasks); }
    }

    [PostgresFact]
    public async Task QuotaBackoff_StartsAtSixtySecondsAndDoubles()
    {
        var user = Guid.NewGuid();
        await Devices(user, 1);
        var dispatch = await Dispatch(Request(user));
        var sender = new Push { Handler = (_, _) => Task.FromResult(PushDeliveryResult.Failure("FCM_QUOTA_EXCEEDED", "Quota exceeded.", true, retryAfter: TimeSpan.FromSeconds(60))) };
        await Process(sender);
        await using (var db = Db())
            Assert.Equal(_clock.GetUtcNow().AddSeconds(60), (await db.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId)).NextRetryAt);
        _clock.Advance(60);
        await Process(sender);
        await using (var db = Db())
            Assert.Equal(_clock.GetUtcNow().AddSeconds(120), (await db.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId)).NextRetryAt);
        _clock.Advance(120);
        await Process(sender);
        Assert.False(await Process(sender));
        Assert.Equal(3, sender.Calls);
    }

    [PostgresFact]
    public async Task ExpiredOwner_CannotInvalidateTokenAfterNewOwnerSucceeded()
    {
        var user = Guid.NewGuid();
        var device = Assert.Single(await Devices(user, 1));
        var dispatch = await Dispatch(Request(user));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<PushDeliveryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = new Push { Handler = async (_, token) => { entered.TrySetResult(); return await release.Task.WaitAsync(token); } };
        var oldTask = Process(old);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _clock.Advance(_settings.LeaseSeconds + 1);
            await Process(new Push());
            release.TrySetResult(PushDeliveryResult.Failure("FCM_UNREGISTERED", "Obsolete token result.", invalidateToken: true));
            await oldTask;
        }
        finally { release.TrySetResult(PushDeliveryResult.Sent("late")); await oldTask; }
        await using var verify = Db();
        Assert.True((await verify.DeviceTokens.SingleAsync(d => d.Id == device.Id)).IsActive);
        Assert.Equal(DeliveryStatus.Sent, (await verify.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId)).Status);
        Assert.Equal(0, await verify.AuditLogs.CountAsync(a => a.EntityId == device.Id.ToString()));
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks = DateTimeOffset.UtcNow.Ticks / 10 * 10;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(int seconds) => Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(seconds).Ticks);
    }
    private sealed class NoEmail : IEmailDeliverySender
    {
        public Task<EmailDeliveryResult> SendAsync(EmailDeliveryMessage message, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Push tests must not call SMTP.");
    }
    private sealed class Push : IPushDeliverySender
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public Func<PushDeliveryMessage,CancellationToken,Task<PushDeliveryResult>> Handler { get; init; }
            = (_,_) => Task.FromResult(PushDeliveryResult.Sent("accepted"));
        public Task<PushDeliveryResult> SendAsync(PushDeliveryMessage message, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Handler(message,cancellationToken);
        }
    }
    private sealed class SaveGate(int count) : SaveChangesInterceptor
    {
        private int _arrived;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrived) == count) _release.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }
}
