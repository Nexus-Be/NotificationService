namespace Notification.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Services.Dispatches;
using Notification.Infrastructure.Persistence;
using Notification.Api.Common;
using Notification.Api.Controllers.Public;
using Notification.Api.Middleware;
using Notification.Application.Interfaces;
using Notification.Application.Services.Email;
using Notification.Application.Services.Notifications;
using Notification.Infrastructure.Services.Email;
using Xunit;

public sealed class EmailApiIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly Mock<IEmailSenderService> _sender = new();
    private readonly EmailBackgroundQueue _queue = new();
    private readonly Mock<IUserNotificationService> _notifications = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(EmailController).Assembly)
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiValidationProblemFactory.Create);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        _sender.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        builder.Services.AddSingleton(_sender.Object);
        builder.Services.AddSingleton<IEmailBackgroundQueue>(_queue);
        builder.Services.AddScoped<IEmailNotificationService, EmailNotificationService>();
        builder.Services.AddSingleton(_notifications.Object);
        var databaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(databaseName));
        builder.Services.AddScoped<INotificationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        builder.Services.AddScoped<IDispatchService, DispatchService>();
        _app = builder.Build();
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        _app.UseGlobalExceptionHandler();
        _app.UseMiddleware<UserContextMiddleware>();
        _app.UseSwagger();
        _app.MapControllers();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("registration")]
    [InlineData("password_reset")]
    [InlineData("seller_registration")]
    public async Task Otp_PreservesPurpose_AndReportsQueued(string purpose)
    {
        _client.DefaultRequestHeaders.Add("x-user-id", Guid.NewGuid().ToString());
        _client.DefaultRequestHeaders.Add("x-user-roles", "service");
        var response = await _client.PostAsJsonAsync("/api/v1/notifications/email/otp", new { email = "user@example.com", otpCode = "123456", purpose });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Queued", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(purpose, json.RootElement.GetProperty("purpose").GetString());
        Assert.False(json.RootElement.TryGetProperty("data", out _));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var queued = await _queue.DequeueOtpEmailAsync(timeout.Token);
        Assert.Equal(purpose, queued.Purpose);
        Assert.Equal("Customer", queued.FullName);
    }

    [Fact]
    public async Task Otp_OmittedPurposeAndExpiry_UseDefaults()
    {
        _client.DefaultRequestHeaders.Add("x-user-id", Guid.NewGuid().ToString());
        _client.DefaultRequestHeaders.Add("x-user-roles", "service");
        var response = await _client.PostAsJsonAsync("/api/v1/notifications/email/otp", new { email = "user@example.com", otpCode = "123456" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var queued = await _queue.DequeueOtpEmailAsync(timeout.Token);
        Assert.Equal("registration", queued.Purpose);
        Assert.Equal(5, queued.ExpiresInMinutes);
    }

    [Theory]
    [InlineData("bad", "123456", 5, "registration")]
    [InlineData("user@example.com", "", 5, "registration")]
    [InlineData("user@example.com", "123456", 0, "registration")]
    [InlineData("user@example.com", "123456", 61, "registration")]
    [InlineData("user@example.com", "123456", 5, "unknown")]
    public async Task Otp_InvalidInput_ReturnsProblemDetails(string email, string otpCode, int expiresInMinutes, string purpose)
    {
        _client.DefaultRequestHeaders.Add("x-user-id", Guid.NewGuid().ToString());
        _client.DefaultRequestHeaders.Add("x-user-roles", "service");
        var response = await _client.PostAsJsonAsync("/api/v1/notifications/email/otp", new { email, otpCode, expiresInMinutes, purpose });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("VAL_INVALID_INPUT", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData("/api/v1/internal/notifications/email/otp")]
    [InlineData("/api/v1/internal/notifications/email/send")]
    [InlineData("/api/v1/internal/notifications/email/seller-rejection")]
    public async Task LegacyEmailRoutes_AreNotExposed(string route)
    {
        var response = await _client.PostAsJsonAsync(route, new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_DiscoversAllEmailRoutesWithoutConflicts()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        foreach (var route in new[] { "otp", "send", "seller-rejection" })
            Assert.True(paths.GetProperty("/api/v1/notifications/email/" + route).TryGetProperty("post", out _));
        Assert.DoesNotContain(paths.EnumerateObject(), p => p.Name.StartsWith("/api/v1/internal/notifications/email"));
    }

    [Fact]
    public async Task GeneralEmail_ReturnsPureDto_AndProviderFailureUsesMiddleware()
    {
        var request = new { toEmail = "user@example.com", subject = "Hello", htmlBody = "<p>Hello</p>" };
        var response = await _client.PostAsJsonAsync("/api/v1/notifications/email/send", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Sent", json.RootElement.GetProperty("status").GetString());
        _sender.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        response = await _client.PostAsJsonAsync("/api/v1/notifications/email/send", request);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("EMAIL_SEND_FAILED", error.RootElement.GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData("bad", "Hello", "Body")]
    [InlineData("user@example.com", "", "Body")]
    [InlineData("user@example.com", "Hello", "")]
    public async Task GeneralEmail_RejectsInvalidInput(string toEmail, string subject, string htmlBody)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/notifications/email/send", new { toEmail, subject, htmlBody });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _sender.Verify(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rejection_EncodesUntrustedHtml_AndRequiresReason()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/notifications/email/seller-rejection",
            new { email = "seller@example.com", fullName = "<b>Seller</b>", shopName = "Shop", rejectReason = "<script>bad</script>" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _sender.Verify(s => s.SendEmailAsync("seller@example.com", It.IsAny<string>(),
            It.Is<string>(html => html.Contains("&lt;b&gt;Seller&lt;/b&gt;") && html.Contains("&lt;script&gt;") && !html.Contains("<script>")), It.IsAny<CancellationToken>()), Times.Once);
        response = await _client.PostAsJsonAsync("/api/v1/notifications/email/seller-rejection", new { email = "seller@example.com", rejectReason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_RequiresIdentity_AndPassesAuthenticatedOwner()
    {
        var response = await _client.DeleteAsync("/api/v1/notifications/devices/token");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var userId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Add("x-user-id", userId.ToString());
        _notifications.Setup(s => s.RevokeDeviceAsync(userId, "token", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        response = await _client.DeleteAsync("/api/v1/notifications/devices/token");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        _notifications.Verify(s => s.RevokeDeviceAsync(userId, "token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatch_UsesRealFeatureAndReturnsPureDto()
    {
        var request = DispatchServiceTests.Request();
        var response = await _client.PostAsJsonAsync("/api/v1/notifications/dispatches", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var first = await response.Content.ReadFromJsonAsync<DispatchResponse>();
        Assert.Equal("Sent", first!.DeliveryStatus);
        response = await _client.PostAsJsonAsync("/api/v1/notifications/dispatches", request);
        var second = await response.Content.ReadFromJsonAsync<DispatchResponse>();
        Assert.True(second!.IsReplay);
        Assert.Equal(first.NotificationId, second.NotificationId);
        response = await _client.PostAsJsonAsync("/api/v1/notifications/dispatches", new { templateCode = "ORDER_PAID_IN_APP", data = new { } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        response = await _client.PostAsJsonAsync("/api/v1/notifications/dispatches", request with { TemplateCode = "MISSING" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("TEMPLATE_NOT_FOUND", error.RootElement.GetProperty("errorCode").GetString());
        using var swagger = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json"));
        Assert.True(swagger.RootElement.GetProperty("paths").GetProperty("/api/v1/notifications/dispatches").TryGetProperty("post", out _));
    }

    [Fact]
    public async Task PushDispatch_ReturnsPerDeviceArrayAndRejectsCallerDestination()
    {
        var user = Guid.NewGuid();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DeviceTokens.AddRange(
                new Notification.Domain.Entities.DeviceToken { Id = Guid.NewGuid(), UserId = user, Platform = Notification.Domain.Enums.DevicePlatform.Web, Token = "private-token-web" },
                new Notification.Domain.Entities.DeviceToken { Id = Guid.NewGuid(), UserId = user, Platform = Notification.Domain.Enums.DevicePlatform.Android, Token = "private-token-android" });
            await db.SaveChangesAsync();
        }
        var request = DispatchServiceTests.Request("ORDER_PAID_PUSH") with { UserId = user, Destination = null };
        var response = await _client.PostAsJsonAsync("/api/v1/notifications/dispatches", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DispatchResponse>();
        Assert.Equal("Push", body!.Channel);
        Assert.Equal(2, body.Deliveries.Count);
        Assert.Equal(body.DeliveryId, body.Deliveries[0].DeliveryId);
        response = await _client.PostAsJsonAsync("/api/v1/notifications/dispatches", request with { SourceEventId = Guid.NewGuid(), Destination = "other@example.com" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
