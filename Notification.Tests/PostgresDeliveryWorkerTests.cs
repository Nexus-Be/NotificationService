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
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class PostgresDeliveryWorkerTests : IAsyncLifetime
{
    private string? _admin;
    private string? _database;
    private string? _connection;
    private readonly ManualClock _clock = new();
    private readonly DeliveryWorkerSettings _settings = new();

    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_worker_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"", admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using var db = CreateDb();
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

    private AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection).Options);

    private async Task<DispatchResponse> Pending()
    {
        await using var db = CreateDb();
        return await new DispatchService(db).CreateAsync(DispatchServiceTests.Request("ORDER_PAID_EMAIL"));
    }

    private async Task<bool> Process(FakeSender sender, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDb();
        return await new DeliveryService(db, sender, _settings, _clock).ProcessNextAsync(cancellationToken);
    }

    [PostgresFact]
    public async Task Success_PersistsClaimResultLogAndCompletedNotification()
    {
        var dispatch = await Pending();
        var sender = new FakeSender();
        Assert.True(await Process(sender));
        Assert.False(await Process(sender));
        Assert.Equal(1, sender.Calls);
        await using var db = CreateDb();
        var delivery = await db.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId);
        Assert.Equal(DeliveryStatus.Sent, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Equal(3, delivery.RowVersion);
        Assert.Null(delivery.NextRetryAt);
        Assert.Null(delivery.ErrorCode);
        Assert.Equal(NotificationStatus.Completed, (await db.Notifications.SingleAsync(n => n.Id == dispatch.NotificationId)).Status);
        var actions = await db.NotificationLogs.Where(l => l.DeliveryId == delivery.Id).Select(l => l.Action).ToListAsync();
        Assert.Contains(LogAction.Dispatching, actions);
        Assert.Contains(LogAction.Sent, actions);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task TemporaryFailures_RespectDueTimesAndStopAtThreeTotalAttempts()
    {
        var dispatch = await Pending();
        var sender = new FakeSender { Handler = (_, _) => Task.FromResult(EmailDeliveryResult.Failure("SMTP_450", "Temporary SMTP failure.", true)) };
        Assert.True(await Process(sender));
        Assert.False(await Process(sender));
        await using (var db = CreateDb())
        {
            var first = await db.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId);
            Assert.Equal(DeliveryStatus.Pending, first.Status);
            Assert.Equal(_clock.GetUtcNow().AddSeconds(10), first.NextRetryAt);
        }
        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(await Process(sender));
        await using (var db = CreateDb())
            Assert.Equal(_clock.GetUtcNow().AddSeconds(20), (await db.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId)).NextRetryAt);
        _clock.Advance(TimeSpan.FromSeconds(20));
        Assert.True(await Process(sender));
        _clock.Advance(TimeSpan.FromDays(1));
        Assert.False(await Process(sender));
        Assert.Equal(3, sender.Calls);
        Assert.Single(sender.Messages.Select(m => m.MessageId).Distinct());
        await using var verify = CreateDb();
        var terminal = await verify.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId);
        Assert.Equal(DeliveryStatus.Failed, terminal.Status);
        Assert.Equal(3, terminal.AttemptCount);
        Assert.Null(terminal.NextRetryAt);
        Assert.Equal("SMTP_450", terminal.ErrorCode);
        Assert.Equal(NotificationStatus.Failed, (await verify.Notifications.SingleAsync(n => n.Id == dispatch.NotificationId)).Status);
        Assert.Equal(2, await verify.NotificationLogs.CountAsync(l => l.DeliveryId == terminal.Id && l.Action == LogAction.Retry));
    }

    [PostgresFact]
    public async Task PermanentFailure_DoesNotRetry_AndRetrySuccessClearsErrors()
    {
        var dispatch = await Pending();
        var permanent = new FakeSender { Handler = (_, _) => Task.FromResult(EmailDeliveryResult.Failure("SMTP_550", "Recipient rejected.")) };
        await Process(permanent);
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.False(await Process(permanent));
        Assert.Equal(1, permanent.Calls);
        await using (var db = CreateDb())
            Assert.Equal(DeliveryStatus.Failed, (await db.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId)).Status);

        var retryDispatch = await Pending();
        var transient = new FakeSender { Handler = (_, _) => Task.FromResult(EmailDeliveryResult.Failure("SMTP_451", "Temporary error.", true)) };
        await Process(transient);
        _clock.Advance(TimeSpan.FromSeconds(10));
        await Process(new FakeSender());
        await using var verify = CreateDb();
        var sent = await verify.NotificationDeliveries.SingleAsync(d => d.Id == retryDispatch.DeliveryId);
        Assert.Equal(DeliveryStatus.Sent, sent.Status);
        Assert.Equal(2, sent.AttemptCount);
        Assert.Null(sent.ErrorCode);
        Assert.Null(sent.ErrorMessage);
        Assert.Null(sent.NextRetryAt);
    }

    [PostgresFact]
    public async Task EightWorkers_OnlyOneCallsProviderForActiveLease()
    {
        await Pending();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new FakeSender { Handler = async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); return EmailDeliveryResult.Sent(); } };
        var tasks = Enumerable.Range(0, 8).Select(_ => Process(sender)).ToArray();
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
    public async Task ExpiredLease_RecoversAndFencesLateOwnerResult()
    {
        var dispatch = await Pending();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<EmailDeliveryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldSender = new FakeSender { Handler = async (_, token) => { entered.TrySetResult(); return await release.Task.WaitAsync(token); } };
        var oldAttempt = Process(oldSender);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _clock.Advance(TimeSpan.FromSeconds(_settings.LeaseSeconds + 1));
            Assert.True(await Process(new FakeSender()));
            release.TrySetResult(EmailDeliveryResult.Failure("OLD_RESULT", "Obsolete transient result.", true));
            await oldAttempt;
        }
        finally { release.TrySetResult(EmailDeliveryResult.Sent()); await oldAttempt; }
        await using var verify = CreateDb();
        var delivery = await verify.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId);
        Assert.Equal(DeliveryStatus.Sent, delivery.Status);
        Assert.Equal(2, delivery.AttemptCount);
        Assert.Null(delivery.ErrorCode);
        Assert.Equal(1, await verify.NotificationLogs.CountAsync(l => l.DeliveryId == delivery.Id && l.Action == LogAction.Sent));
        Assert.Equal(0, await verify.NotificationLogs.CountAsync(l => l.DeliveryId == delivery.Id && l.Action == LogAction.Retry));
    }

    [PostgresFact]
    public async Task ExpiredThirdAttempt_BecomesFailedWithoutFourthSubmission()
    {
        var dispatch = await Pending();
        await using (var db = CreateDb())
        {
            var delivery = await db.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId);
            delivery.Status = DeliveryStatus.Sending;
            delivery.AttemptCount = 3;
            delivery.LastAttemptAt = _clock.GetUtcNow().AddSeconds(-_settings.LeaseSeconds - 1);
            await db.SaveChangesAsync();
        }
        var sender = new FakeSender();
        Assert.True(await Process(sender));
        Assert.Equal(0, sender.Calls);
        await using var verify = CreateDb();
        var failed = await verify.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId);
        Assert.Equal(DeliveryStatus.Failed, failed.Status);
        Assert.Equal(3, failed.AttemptCount);
        Assert.Equal("RETRY_LIMIT_EXCEEDED", failed.ErrorCode);
    }

    [PostgresFact]
    public async Task HostCancellation_LeavesDurableLeaseForRecovery()
    {
        var dispatch = await Pending();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new FakeSender { Handler = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return EmailDeliveryResult.Sent(); } };
        using var shutdown = new CancellationTokenSource();
        var processing = Process(sender, shutdown.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        shutdown.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        await using (var db = CreateDb())
        {
            var leased = await db.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId);
            Assert.Equal(DeliveryStatus.Sending, leased.Status);
            Assert.Equal(1, leased.AttemptCount);
        }
        Assert.False(await Process(new FakeSender()));
        _clock.Advance(TimeSpan.FromSeconds(_settings.LeaseSeconds + 1));
        Assert.True(await Process(new FakeSender()));
        await using var verify = CreateDb();
        Assert.Equal(DeliveryStatus.Sent, (await verify.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId)).Status);
    }

    [PostgresFact]
    public async Task SenderTimeout_IsTemporaryAndSchedulesBackoff()
    {
        var dispatch = await Pending();
        _settings.AttemptTimeoutSeconds = 1;
        var sender = new FakeSender { Handler = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return EmailDeliveryResult.Sent(); } };
        await Process(sender).WaitAsync(TimeSpan.FromSeconds(10));
        await using var verify = CreateDb();
        var delivery = await verify.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId);
        Assert.Equal(DeliveryStatus.Pending, delivery.Status);
        Assert.Equal("EMAIL_TIMEOUT", delivery.ErrorCode);
        Assert.Equal(_clock.GetUtcNow().AddSeconds(10), delivery.NextRetryAt);
    }

    [PostgresFact]
    public async Task DisabledChannel_PausesPendingWithoutConsumingAttempts()
    {
        var dispatch = await Pending();
        await using (var db = CreateDb())
        {
            (await db.NotificationChannels.SingleAsync(c => c.Code == ChannelType.Email)).IsDisabled = true;
            await db.SaveChangesAsync();
        }
        var sender = new FakeSender();
        Assert.False(await Process(sender));
        Assert.Equal(0, sender.Calls);
        await using var verify = CreateDb();
        var delivery = await verify.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId);
        Assert.Equal(DeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0, delivery.AttemptCount);
    }

    [PostgresFact]
    public async Task ConcurrentSiblingDeliveries_AggregatePartialWithoutLostUpdates()
    {
        var dispatch = await Pending();
        await using (var db = CreateDb())
        {
            db.NotificationDeliveries.Add(new NotificationDelivery
            {
                Id = Guid.NewGuid(), NotificationId = dispatch.NotificationId, ChannelCode = ChannelType.Email,
                Destination = "second@example.test", Status = DeliveryStatus.Pending, RowVersion = 1
            });
            await db.SaveChangesAsync();
        }
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var sender = new FakeSender { Handler = async (message, token) =>
        {
            if (Interlocked.Increment(ref entered) == 2) bothEntered.TrySetResult();
            firstEntered.TrySetResult();
            await release.Task.WaitAsync(token);
            return message.Recipient == "second@example.test" ? EmailDeliveryResult.Failure("SMTP_550", "Recipient rejected.") : EmailDeliveryResult.Sent();
        } };
        var first = Process(sender);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = Process(sender);
        try { await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { release.TrySetResult(); await Task.WhenAll(first, second); }
        await using var verify = CreateDb();
        Assert.Equal(NotificationStatus.Partial, (await verify.Notifications.SingleAsync(n => n.Id == dispatch.NotificationId)).Status);
        var states = await verify.NotificationDeliveries.Where(d => d.NotificationId == dispatch.NotificationId).Select(d => d.Status).ToListAsync();
        Assert.Contains(DeliveryStatus.Sent, states);
        Assert.Contains(DeliveryStatus.Failed, states);
    }

    [PostgresFact]
    public async Task ClaimLogFailure_RollsBackClaimBeforeProviderCall()
    {
        var dispatch = await Pending();
        var sender = new FakeSender();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection).AddInterceptors(new RejectClaimLog()).Options;
        await using (var db = new AppDbContext(options))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new DeliveryService(db, sender, _settings, _clock).ProcessNextAsync());
        Assert.Equal(0, sender.Calls);
        await using var verify = CreateDb();
        var delivery = await verify.NotificationDeliveries.SingleAsync(d => d.Id == dispatch.DeliveryId);
        Assert.Equal(DeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0, delivery.AttemptCount);
        Assert.Equal(1, delivery.RowVersion);
        Assert.Equal(0, await verify.NotificationLogs.CountAsync(l => l.DeliveryId == delivery.Id && l.Action == LogAction.Dispatching));
    }

    private sealed class RejectClaimLog : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Test claim log persistence failure.");
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks = DateTimeOffset.UtcNow.Ticks / 10 * 10;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(TimeSpan value) => Interlocked.Add(ref _ticks, value.Ticks);
    }

    private sealed class FakeSender : IEmailDeliverySender
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public ConcurrentQueue<EmailDeliveryMessage> Messages { get; } = new();
        public Func<EmailDeliveryMessage, CancellationToken, Task<EmailDeliveryResult>> Handler { get; init; }
            = (_, _) => Task.FromResult(EmailDeliveryResult.Sent());
        public Task<EmailDeliveryResult> SendAsync(EmailDeliveryMessage message, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            Messages.Enqueue(message);
            return Handler(message, cancellationToken);
        }
    }
}
