namespace Notification.Tests;

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
using Notification.Application.Services.Dispatches;
using Notification.Application.Services.Notifications;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class NotificationDetailApiIntegrationTests : IAsyncLifetime
{
    private const string Root = "/api/v1/notifications";
    private readonly Guid _user = Guid.NewGuid();
    private string? _admin, _database, _connection;
    private WebApplication? _app;
    private HttpClient _client = null!;
    private AppDbContext Db(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if (string.IsNullOrWhiteSpace(_admin)) return;
        _database = "notification_detail_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"", admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(NotificationsController).Assembly)
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiValidationProblemFactory.Create);
        builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connection));
        builder.Services.AddScoped<INotificationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddApplicationServices();
        _app = builder.Build(); _app.UseGlobalExceptionHandler(); _app.UseMiddleware<UserContextMiddleware>(); _app.UseSwagger(); _app.MapControllers();
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        Identity(_user);
    }
    public async Task DisposeAsync()
    {
        _client?.Dispose(); if (_app is not null) await _app.DisposeAsync();
        if (_database is null || _admin is null) return;
        NpgsqlConnection.ClearAllPools(); await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("DROP DATABASE \"" + _database + "\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }
    private void Identity(Guid? user, string role = "buyer")
    {
        _client.DefaultRequestHeaders.Remove("x-user-id"); _client.DefaultRequestHeaders.Remove("x-user-roles");
        if (user.HasValue) { _client.DefaultRequestHeaders.Add("x-user-id", user.ToString()); _client.DefaultRequestHeaders.Add("x-user-roles", role); }
    }
    private static UpdatePreferencesRequest Preference(string type = "OrderShipped", string channel = "Email", bool disabled = true)
        => new(new[] { new UpdatePreferenceItemRequest(type, channel, disabled) });
    private static async Task Check(HttpResponseMessage response, HttpStatusCode expected, string? code = null)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}");
        if (code is not null) Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("errorCode").GetString());
    }
    private async Task<Guid> Notification(bool read = true, bool hidden = false, Guid? owner = null)
    {
        await using var db = Db(); var id = Guid.NewGuid();
        db.Notifications.Add(new()
        {
            Id = id,
            UserId = owner ?? _user,
            Title = "Test",
            Body = "Body",
            IsRead = read,
            ReadAt = read ? DateTimeOffset.UtcNow : null,
            IsDeleted = hidden,
            Status = NotificationStatus.Pending
        });
        await db.SaveChangesAsync(); return id;
    }
    private static NotificationPreference Stored(Guid user, NotificationType type, ChannelType channel, bool disabled = true, bool hidden = false)
        => new() { Id = Guid.NewGuid(), UserId = user, NotificationType = type, ChannelCode = channel, IsDisabled = disabled, IsDeleted = hidden };


    [PostgresFact]
    public async Task OwnerDetail_ReturnsPureDtoAndNoStore_WithoutReadingOrAddingDeliveries()
    {
        var id = await Notification(read: false); await using var db = Db(); var before = await db.NotificationDeliveries.CountAsync();
        var response = await _client.GetAsync(Root + "/" + id); await Check(response, HttpStatusCode.OK); Assert.True(response.Headers.CacheControl!.NoStore);
        var item = (await response.Content.ReadFromJsonAsync<InAppNotificationItemResponse>())!; Assert.Equal(id, item.Id); Assert.Equal(_user, item.UserId); Assert.False(item.IsRead); Assert.Null(item.ReadAt);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement; Assert.False(json.TryGetProperty("data", out _));
        Assert.False((await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == id)).IsRead); Assert.Equal(before, await db.NotificationDeliveries.CountAsync());
    }
    [PostgresFact]
    public async Task ForeignMissingDeletedAndEmptyId_AreIndistinguishable404()
    {
        var foreign = await Notification(owner: Guid.NewGuid()); var hidden = await Notification(hidden: true);
        foreach (var id in new[] { foreign, hidden, Guid.NewGuid(), Guid.Empty }) await Check(await _client.GetAsync(Root + "/" + id), HttpStatusCode.NotFound, "NOTIFICATION_NOT_FOUND");
    }
    [PostgresFact]
    public async Task AnonymousCannotReadDetail()
    {
        var id = await Notification(); Identity(null); await Check(await _client.GetAsync(Root + "/" + id), HttpStatusCode.Unauthorized);
    }
    [PostgresFact]
    public async Task ActivePushOnlyDetail_IsOwnedButNeverAddedToBellOrReadActions()
    {
        var id = await Notification(); await using (var db = Db()) await db.Notifications.Where(n => n.Id == id).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsInboxVisible, false));
        await Check(await _client.GetAsync(Root + "/" + id), HttpStatusCode.OK);
        var inbox = (await _client.GetFromJsonAsync<Notification.Application.Common.Models.PagedData<InAppNotificationItemResponse>>(Root + "/me"))!; Assert.DoesNotContain(inbox.Items, n => n.Id == id);
        await Check(await _client.PutAsJsonAsync(Root + "/" + id + "/read-states", new { isRead = false }), HttpStatusCode.NotFound, "NOTIFICATION_NOT_FOUND");
    }
    [PostgresFact]
    public async Task DetailAndReadState_AreSeparateExplicitOperations()
    {
        var id = await Notification(read: false); await Check(await _client.GetAsync(Root + "/" + id), HttpStatusCode.OK);
        await Check(await _client.PutAsJsonAsync(Root + "/" + id + "/read-states", new { isRead = true }), HttpStatusCode.OK);
        Assert.True((await _client.GetFromJsonAsync<InAppNotificationItemResponse>(Root + "/" + id))!.IsRead);
    }
    [PostgresFact]
    public async Task DetailWorksForAnyAuthenticatedOwnerRole_NotForeignAdmin()
    {
        var id = await Notification(); foreach (var role in new[] { "buyer", "seller", "admin" }) { Identity(_user, role); await Check(await _client.GetAsync(Root + "/" + id), HttpStatusCode.OK); }
        Identity(Guid.NewGuid(), "admin"); await Check(await _client.GetAsync(Root + "/" + id), HttpStatusCode.NotFound, "NOTIFICATION_NOT_FOUND");
    }
    [PostgresFact]
    public async Task FcmIdsResolveAcrossNotificationTypes_WithoutFeatureReceiptOrSchemaChange()
    {
        foreach (var type in new[] { NotificationType.OrderPaid, NotificationType.AuctionWon, NotificationType.OrderBuyerCancelled })
        {
            var id = await Notification(); await using (var db = Db()) await db.Notifications.Where(n => n.Id == id).ExecuteUpdateAsync(s => s.SetProperty(n => n.NotificationType, type));
            Assert.Equal(type.ToString(), (await _client.GetFromJsonAsync<InAppNotificationItemResponse>(Root + "/" + id))!.NotificationType);
        }
        await using var verify = Db(); Assert.False(verify.Database.HasPendingModelChanges());
    }
    [PostgresFact]
    public async Task SwaggerExposesCommonDetailAndExistingInboxRoutes()
    {
        var json = await _client.GetStringAsync("/swagger/v1/swagger.json"); Assert.Contains(Root + "/{id}", json); Assert.Contains(Root + "/me", json); Assert.Contains(Root + "/devices", json);
    }
}
