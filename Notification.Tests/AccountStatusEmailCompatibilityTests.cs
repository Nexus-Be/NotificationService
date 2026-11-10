namespace Notification.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Notification.Api.Common;
using Notification.Api.Controllers.Public;
using Notification.Api.Middleware;
using Notification.Application.Interfaces;
using Notification.Application.Services.Deliveries;
using Notification.Application.Services.Email;
using Notification.Domain.Exceptions;
using Xunit;

public sealed class AccountStatusEmailCompatibilityTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly Sender _sender = new();
    private const string Root = "/api/v1/notifications/email/";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(EmailController).Assembly)
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiValidationProblemFactory.Create);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSingleton(Mock.Of<IEmailSenderService>());
        builder.Services.AddSingleton<IEmailDeliverySender>(_sender);
        builder.Services.AddScoped<IEmailNotificationService, EmailNotificationService>();
        _app = builder.Build();
        _app.UseGlobalExceptionHandler();
        _app.UseMiddleware<UserContextMiddleware>();
        _app.UseSwagger();
        _app.MapControllers();
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        Role("admin");
    }

    public async Task DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); }

    private void Role(string? role)
    {
        _client.DefaultRequestHeaders.Remove("x-user-id");
        _client.DefaultRequestHeaders.Remove("x-user-roles");
        if (role is null) return;
        _client.DefaultRequestHeaders.Add("x-user-id", Guid.NewGuid().ToString());
        _client.DefaultRequestHeaders.Add("x-user-roles", role);
    }

    [Fact]
    public async Task Ban_UsesExactCallerPayload_EncodesContent_AndReturnsPureSentResponse()
    {
        var response = await _client.PostAsJsonAsync(Root + "user-banned", new { email = " owner@example.com ", fullName = "<img src=x>", reason = "<script>private()</script>" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<EmailDispatchResponse>())!;
        Assert.Equal("owner@example.com", result.Recipient);
        Assert.Equal("Sent", result.Status);
        var message = Assert.Single(_sender.Messages);
        Assert.Contains("khóa tài khoản", message.Subject);
        Assert.Contains("&lt;img", message.HtmlBody);
        Assert.Contains("&lt;script&gt;", message.HtmlBody);
        Assert.DoesNotContain("<script>", message.HtmlBody);
        Assert.NotEqual(Guid.Empty, message.DeliveryId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<b>Reviewed</b>")]
    public async Task Unban_AllowsOptionalReason_AndUsesDistinctRestorationContent(string? reason)
    {
        Role("service");
        var response = await _client.PostAsJsonAsync(Root + "user-unbanned", new { email = "owner@example.com", fullName = "Owner", reason });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var message = Assert.Single(_sender.Messages);
        Assert.Contains("mở khóa", message.Subject);
        Assert.Contains("đã được mở khóa", message.HtmlBody);
        Assert.DoesNotContain("do vi phạm", message.HtmlBody);
        if (reason is null or "") Assert.DoesNotContain("Lý do", message.HtmlBody);
        else Assert.Contains("&lt;b&gt;Reviewed&lt;/b&gt;", message.HtmlBody);
    }

    [Theory]
    [InlineData("user-banned", null, 401)]
    [InlineData("user-unbanned", null, 401)]
    [InlineData("user-banned", "buyer", 403)]
    [InlineData("user-unbanned", "buyer", 403)]
    [InlineData("user-banned", "seller", 403)]
    [InlineData("user-unbanned", "staff", 403)]
    public async Task NonPrivilegedCallers_CannotSend(string route, string? role, int status)
    {
        Role(role);
        var response = await _client.PostAsJsonAsync(Root + route, new { email = "owner@example.com", fullName = "Owner", reason = "Test reason" });
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Empty(_sender.Messages);
    }

    [Theory]
    [InlineData("user-banned", "invalid", "Owner", "reason")]
    [InlineData("user-banned", "owner@example.com", "Owner", " ")]
    [InlineData("user-banned", "owner@example.com", "bad\nname", "reason")]
    [InlineData("user-unbanned", "invalid", "Owner", null)]
    [InlineData("user-unbanned", "owner@example.com", "Owner", "bad\u0001reason")]
    public async Task InvalidFields_DoNotReachProvider(string route, string email, string name, string? reason)
    {
        var response = await _client.PostAsJsonAsync(Root + route, new { email, fullName = name, reason });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_sender.Messages);
    }

    [Theory]
    [InlineData("user-banned")]
    [InlineData("user-unbanned")]
    public async Task UnknownFields_AndOversizedReasons_AreRejected(string route)
    {
        var unknown = await _client.PostAsJsonAsync(Root + route, new { email = "owner@example.com", reason = "reason", userId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var oversized = await _client.PostAsJsonAsync(Root + route, new { email = "owner@example.com", reason = new string('x', 1001) });
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Empty(_sender.Messages);
    }

    [Theory]
    [InlineData("SMTP_554", 500, "EMAIL_SEND_FAILED")]
    [InlineData("SMTP_AUTH_FAILED", 500, "EMAIL_SEND_FAILED")]
    [InlineData("SMTP_NOT_CONFIGURED", 503, "EMAIL_PROVIDER_UNAVAILABLE")]
    public async Task ProviderFailure_NeverPretendsSentOrExposesProviderDetails(string code, int status, string errorCode)
    {
        _sender.Result = EmailDeliveryResult.Failure(code, "Private provider diagnostics");
        var response = await _client.PostAsJsonAsync(Root + "user-banned", new { email = "owner@example.com", reason = "Test reason" });
        Assert.Equal(status, (int)response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(errorCode, JsonDocument.Parse(text).RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("Private provider diagnostics", text);
        Assert.Single(_sender.Messages);
    }

    [Fact]
    public async Task NoStrictProvider_DoesNotFallBackToLegacySimulation()
    {
        var service = new EmailNotificationService(Mock.Of<IEmailSenderService>());
        await Assert.ThrowsAsync<DomainException>(() => service.DispatchUserUnbannedAsync(new() { Email = "owner@example.com" }));
    }

    [Fact]
    public async Task Swagger_ExposesBothExactCompatibilityRoutes()
    {
        var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains(Root + "user-banned", swagger);
        Assert.Contains(Root + "user-unbanned", swagger);
    }

    private sealed class Sender : IEmailDeliverySender
    {
        public List<EmailDeliveryMessage> Messages { get; } = new();
        public EmailDeliveryResult Result { get; set; } = EmailDeliveryResult.Sent();
        public Task<EmailDeliveryResult> SendAsync(EmailDeliveryMessage message, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Messages.Add(message); return Task.FromResult(Result); }
    }
}
