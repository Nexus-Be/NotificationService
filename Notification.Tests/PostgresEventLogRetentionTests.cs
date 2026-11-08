namespace Notification.Tests;

using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Notification.Application.Services.EventLogs;
using Notification.Domain.Entities;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class PostgresEventLogRetentionTests : IAsyncLifetime
{
    private string? _admin, _database, _connection;
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-11-08T12:00:00+07:00"));
    private DateTimeOffset Cutoff => _clock.GetUtcNow().ToUniversalTime().AddDays(-90);
    private AppDbContext Db(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }
    private static EventLogRetentionSettings Settings(int batch = 500, int max = 20) => new() { Enabled = true, BatchSize = batch, MaxBatchesPerRun = max };
    private EventLogRetentionService Service(AppDbContext db, EventLogRetentionSettings? settings = null) => new(db,settings ?? Settings(),_clock);
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_retention_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"",admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using var db = Db(); await db.Database.MigrateAsync();
    }
    public async Task DisposeAsync()
    {
        if (_database is null || _admin is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("DROP DATABASE \"" + _database + "\" WITH (FORCE)",admin).ExecuteNonQueryAsync();
    }
    private EventLog Row(DateTimeOffset created, DateTimeOffset? occurred = null, bool hidden = false) => new()
    {
        Id = Guid.NewGuid(), SourceService = "retention-test", EventType = "TestEvent", CreatedAt = created.ToUniversalTime(),
        UpdatedAt = created.ToUniversalTime(), OccurredAt = (occurred ?? created).ToUniversalTime(), IsDeleted = hidden,
        EventId = Guid.NewGuid(), Payload = """{"test":"payload"}"""
    };
    private async Task Seed(params EventLog[] rows)
    {
        await using var db = Db(); db.EventLogs.AddRange(rows); await db.SaveChangesAsync();
    }

    [PostgresFact]
    public async Task Boundary_UsesServerCreatedAt_StrictMicrosecondsAndUtc_NotProducerOccurredAt()
    {
        var expired = Row(Cutoff.AddTicks(-10),_clock.GetUtcNow().AddDays(10));
        var exact = Row(Cutoff);
        var recent = Row(Cutoff.AddTicks(10));
        var late = Row(_clock.GetUtcNow(),_clock.GetUtcNow().AddYears(-2));
        await Seed(expired,exact,recent,late);
        await using var db = Db(); var result = await Service(db).CleanupAsync();
        Assert.Equal(Cutoff,result.Cutoff); Assert.Equal(TimeSpan.Zero,result.Cutoff.Offset);
        Assert.Equal(1,result.DeletedCount);
        var kept = await db.EventLogs.AsNoTracking().Select(e => e.Id).ToListAsync();
        Assert.DoesNotContain(expired.Id,kept); Assert.Contains(exact.Id,kept); Assert.Contains(recent.Id,kept); Assert.Contains(late.Id,kept);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task PurgesHiddenAndLegacyRows_LeavesAllOtherTablesUntouched()
    {
        var hidden = Row(Cutoff.AddDays(-1),hidden:true); var legacy = Row(Cutoff.AddDays(-2)); legacy.EventId = null;
        await Seed(hidden,legacy);
        await using var db = Db();
        var audits = await db.AuditLogs.CountAsync(); var notifications = await db.Notifications.IgnoreQueryFilters().CountAsync();
        var logs = await db.NotificationLogs.CountAsync(); var deliveries = await db.NotificationDeliveries.CountAsync();
        Assert.Equal(2,(await Service(db).CleanupAsync()).DeletedCount);
        Assert.Empty(await db.EventLogs.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(audits,await db.AuditLogs.CountAsync()); Assert.Equal(notifications,await db.Notifications.IgnoreQueryFilters().CountAsync());
        Assert.Equal(logs,await db.NotificationLogs.CountAsync()); Assert.Equal(deliveries,await db.NotificationDeliveries.CountAsync());
    }

    [PostgresFact]
    public async Task BoundedBatches_CanResumeBacklogInLaterRun()
    {
        await Seed(Enumerable.Range(0,7).Select(i => Row(Cutoff.AddMinutes(-i-1))).ToArray());
        await using var db = Db();
        var first = await Service(db,Settings(2,2)).CleanupAsync();
        Assert.Equal(4,first.DeletedCount); Assert.Equal(2,first.BatchesExecuted); Assert.True(first.ReachedBatchLimit);
        Assert.Equal(3,await db.EventLogs.CountAsync());
        var next = await Service(db,Settings(2,2)).CleanupAsync();
        Assert.Equal(3,next.DeletedCount); Assert.False(next.ReachedBatchLimit); Assert.Empty(await db.EventLogs.ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentInstances_DeleteEachExpiredRowOnce_AndKeepFreshRows()
    {
        var fresh = Row(_clock.GetUtcNow());
        await Seed(Enumerable.Range(0,50).Select(i => Row(Cutoff.AddMinutes(-i-1))).Append(fresh).ToArray());
        var results = await Task.WhenAll(Enumerable.Range(0,4).Select(async _ =>
        {
            await using var db = Db(); return await Service(db,Settings(7)).CleanupAsync();
        })).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(50,results.Sum(r => r.DeletedCount));
        await using var verify = Db(); Assert.Equal(fresh.Id,(await verify.EventLogs.SingleAsync()).Id);
    }

    [PostgresFact]
    public async Task LockedExpiredRows_AreSkippedAndPurgedOnLaterCycle()
    {
        var locked = Row(Cutoff.AddDays(-2)); var other = Row(Cutoff.AddDays(-1)); await Seed(locked,other);
        await using var locker = Db(); await using var tx = await locker.Database.BeginTransactionAsync();
        await locker.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"EventLogs\" WHERE \"Id\"={locked.Id} FOR UPDATE");
        await using (var db = Db())
        {
            Assert.Equal(1,(await Service(db).CleanupAsync().WaitAsync(TimeSpan.FromSeconds(3))).DeletedCount);
            Assert.Equal(locked.Id,(await db.EventLogs.SingleAsync()).Id);
        }
        await tx.CommitAsync();
        await using var next = Db(); Assert.Equal(1,(await Service(next).CleanupAsync()).DeletedCount);
    }

    [PostgresFact]
    public async Task DisabledAndPreCancelledRuns_DoNotDeleteRows()
    {
        await Seed(Row(Cutoff.AddDays(-1)));
        await using var db = Db();
        var disabled = await Service(db,new EventLogRetentionSettings()).CleanupAsync();
        Assert.Equal(0,disabled.BatchesExecuted);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(db).CleanupAsync(cts.Token));
        Assert.Single(await db.EventLogs.ToListAsync());
    }

    [PostgresFact]
    public async Task CancellationBetweenBatches_PreservesAlreadyCommittedProgress()
    {
        await Seed(Enumerable.Range(0,5).Select(i => Row(Cutoff.AddDays(-1))).ToArray());
        using var cts = new CancellationTokenSource();
        await using (var db = Db(new CancelAfterBatch(cts)))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(db,Settings(2)).CleanupAsync(cts.Token));
        await using var verify = Db(); Assert.Equal(3,await verify.EventLogs.CountAsync());
    }

    [PostgresFact]
    public async Task FailureInLaterBatch_PreservesCommittedEarlierBatch()
    {
        await Seed(Enumerable.Range(0,5).Select(i => Row(Cutoff.AddDays(-1))).ToArray());
        await using (var db = Db(new FailSecondBatch()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db,Settings(2)).CleanupAsync());
        await using var verify = Db(); Assert.Equal(3,await verify.EventLogs.CountAsync());
    }

    [PostgresFact]
    public async Task DatabaseFailure_RollsBackEntireCurrentDeleteStatement()
    {
        var bad = Row(Cutoff.AddDays(-1)); bad.EventType = "FailDelete";
        await Seed(Row(Cutoff.AddDays(-2)),bad);
        await using var db = Db();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION retention_test_fail() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF OLD."EventType" = 'FailDelete' THEN RAISE EXCEPTION 'Simulated retention failure'; END IF;
              RETURN OLD;
            END $$;
            CREATE TRIGGER retention_test_failure BEFORE DELETE ON "EventLogs"
              FOR EACH ROW EXECUTE FUNCTION retention_test_fail();
            """);
        await Assert.ThrowsAsync<PostgresException>(() => Service(db).CleanupAsync());
        Assert.Equal(2,await db.EventLogs.CountAsync());
    }

    [PostgresFact]
    public async Task IngestionIdempotency_EndsOnlyWhenExpiredRecordIsPhysicallyPurged()
    {
        var old = _clock.GetUtcNow().ToUniversalTime().AddDays(-91);
        using var payload = JsonDocument.Parse("""{"orderId":"retention-example"}""");
        var request = new CreateEventLogRequest { EventId = Guid.NewGuid(), SourceService = "commerce", EventType = "OrderPaid",
            OccurredAt = old, Payload = payload.RootElement.Clone() };
        await using var db = Db();
        var first = await new EventLogService(db,new FixedClock(old)).CreateAsync(request);
        var replay = await new EventLogService(db,_clock).CreateAsync(request);
        Assert.True(replay.IsReplay); Assert.Equal(first.Id,replay.Id);
        Assert.Equal(1,(await Service(db).CleanupAsync()).DeletedCount);
        var again = await new EventLogService(db,_clock).CreateAsync(request);
        Assert.False(again.IsReplay); Assert.NotEqual(first.Id,again.Id);
        Assert.Equal(0,(await Service(db).CleanupAsync()).DeletedCount);
        Assert.Single(await db.EventLogs.AsNoTracking().ToListAsync());
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
    private sealed class CancelAfterBatch(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DELETE FROM \"EventLogs\"")) cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }
    private sealed class FailSecondBatch : DbCommandInterceptor
    {
        private int _calls;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DELETE FROM \"EventLogs\"") && ++_calls == 2)
                throw new InvalidOperationException("Simulated later batch failure.");
            return ValueTask.FromResult(result);
        }
    }
}
