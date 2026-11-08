namespace Notification.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Notification.Application.Services.Dispatches;
using Notification.Domain.Entities;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB")))
            Skip = "Requires an isolated PostgreSQL instance via NOTIFICATION_TEST_DB.";
    }
}

public sealed class PostgresDispatchTests : IAsyncLifetime
{
    private string? _connection;
    private string? _database;
    private string? _admin;

    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_dispatch_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"", admin).ExecuteNonQueryAsync();
        var builder = new NpgsqlConnectionStringBuilder(_admin) { Database = _database };
        _connection = builder.ConnectionString;
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }

    private AppDbContext CreateDb(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new AppDbContext(builder.Options);
    }

    public async Task DisposeAsync()
    {
        if (_database is null || _admin is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await new NpgsqlCommand("DROP DATABASE \"" + _database + "\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task ConcurrentSameKey_InsertsExactlyOneNotificationDeliveryAndLog()
    {
        const int count = 8;
        var request = DispatchServiceTests.Request();
        var gate = new SaveGate(count);
        var tasks = Enumerable.Range(0, count).Select(async _ =>
        {
            await using var db = CreateDb(gate);
            return await new DispatchService(db).CreateAsync(request);
        });
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results.Select(r => r.NotificationId).Distinct());
        Assert.Single(results, r => !r.IsReplay);
        Assert.All(results, r => Assert.Equal("Sent", r.DeliveryStatus));
        await using var verify = CreateDb();
        var notification = await verify.Notifications.SingleAsync(n => n.SourceEventId == request.SourceEventId);
        Assert.Single(await verify.NotificationDeliveries.Where(d => d.NotificationId == notification.Id).ToListAsync());
        Assert.Single(await verify.NotificationLogs.Where(l => l.NotificationId == notification.Id).ToListAsync());
    }

    [PostgresFact]
    public async Task DatabaseUniqueIndex_RejectsDuplicateWithoutCommittingPartialRows()
    {
        var request = DispatchServiceTests.Request();
        await using var db = CreateDb();
        var first = await new DispatchService(db).CreateAsync(request);
        var original = await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == first.NotificationId);
        db.ChangeTracker.Clear();
        db.Notifications.Add(new NotificationRecord { Id = Guid.NewGuid(), UserId = original.UserId,
            SourceEventId = original.SourceEventId, TemplateId = original.TemplateId, Status = original.Status });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        await using var verify = CreateDb();
        Assert.Single(await verify.Notifications.Where(n => n.SourceEventId == request.SourceEventId).ToListAsync());
        Assert.Single(await verify.NotificationDeliveries.Where(d => d.NotificationId == first.NotificationId).ToListAsync());
    }

    [PostgresFact]
    public async Task Migration_SeedsMandatoryTemplates_AndPendingEmailReplays()
    {
        await using var db = CreateDb();
        Assert.True((await db.NotificationTemplates.SingleAsync(t => t.Code == "ORDER_PAID_EMAIL")).IsMandatory);
        Assert.True((await db.NotificationTemplates.SingleAsync(t => t.Code == "ORDER_PAID_IN_APP")).IsMandatory);
        var request = DispatchServiceTests.Request("ORDER_PAID_EMAIL");
        var first = await new DispatchService(db).CreateAsync(request);
        await using var replayDb = CreateDb();
        var replay = await new DispatchService(replayDb).CreateAsync(request);
        Assert.True(replay.IsReplay);
        Assert.Equal("Email", replay.Channel);
        Assert.Equal("Pending", replay.DeliveryStatus);
        Assert.Equal(first.NotificationId, replay.NotificationId);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private sealed class SaveGate(int expected) : SaveChangesInterceptor
    {
        private int _arrived;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrived) == expected) _release.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }
}
