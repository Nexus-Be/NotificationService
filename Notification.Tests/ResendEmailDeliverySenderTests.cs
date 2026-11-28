namespace Notification.Tests;

using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Notification.Application.Services.Deliveries;
using Notification.Infrastructure.Services.Email;
using Xunit;

public sealed class ResendEmailDeliverySenderTests
{
    [Fact]
    public async Task Retry_UsesSameIdempotencyKey_AndKeepsProviderId()
    {
        var requests = new List<(string? Key, string? Authorization, string? Body)>();
        using var client = new HttpClient(new StubHandler(async request =>
        {
            requests.Add((request.Headers.GetValues("Idempotency-Key").Single(),
                request.Headers.Authorization?.ToString(),
                await request.Content!.ReadAsStringAsync()));
            return new HttpResponseMessage(HttpStatusCode.Created)
            { Content = new StringContent("{\"id\":\"provider-message-1\"}", Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://api.resend.test/") };
        var sender = Sender(client);
        var message = new EmailDeliveryMessage(Guid.NewGuid(), "buyer@example.test", "Top up", "<p>Paid</p>");

        var first = await sender.SendAsync(message);
        var retry = await sender.SendAsync(message);

        Assert.True(first.Succeeded);
        Assert.Equal("provider-message-1", first.ProviderMessageId);
        Assert.Equal(first.ProviderMessageId, retry.ProviderMessageId);
        Assert.Equal(requests[0].Key, requests[1].Key);
        Assert.Equal(requests[0].Body, requests[1].Body);
        Assert.Equal("Bearer test-key", requests[0].Authorization);
        Assert.Contains("buyer@example.test", requests[0].Body);
    }

    [Theory]
    [InlineData(429, true)]
    [InlineData(503, true)]
    [InlineData(403, false)]
    public async Task ProviderErrors_AreClassifiedWithoutLeakingResponse(int status, bool transient)
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage((HttpStatusCode)status)
            { Content = new StringContent("private provider response") })))
        { BaseAddress = new Uri("https://api.resend.test/") };
        var result = await Sender(client).SendAsync(new(Guid.NewGuid(), "buyer@example.test", "Hello", "Body"));
        Assert.False(result.Succeeded);
        Assert.Equal(transient, result.IsTransient);
        Assert.Equal($"RESEND_HTTP_{status}", result.ErrorCode);
        Assert.DoesNotContain("private", result.ErrorMessage!);
    }

    [Theory]
    [InlineData("concurrent_idempotent_requests", true)]
    [InlineData("invalid_idempotent_request", false)]
    public async Task Conflict_OnlyConcurrentRequestIsRetried(string error, bool transient)
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Conflict)
            { Content = new StringContent($"{{\"name\":\"{error}\"}}") })))
        { BaseAddress = new Uri("https://api.resend.test/") };
        var result = await Sender(client).SendAsync(new(Guid.NewGuid(), "buyer@example.test", "Hello", "Body"));
        Assert.Equal(transient, result.IsTransient);
    }

    [Fact]
    public async Task MissingConfiguration_FailsBeforeAnyRequest()
    {
        using var client = new HttpClient(new StubHandler(_ => throw new Exception("Unexpected request")))
        { BaseAddress = new Uri("https://api.resend.test/") };
        var result = await Sender(client, new ResendOptions()).SendAsync(
            new(Guid.NewGuid(), "buyer@example.test", "Hello", "Body"));
        Assert.Equal("RESEND_NOT_CONFIGURED", result.ErrorCode);
    }

    private static ResendEmailDeliverySender Sender(HttpClient client, ResendOptions? options = null)
    {
        var monitor = new Mock<IOptionsMonitor<ResendOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(options ?? new ResendOptions
        { ApiKey = "test-key", FromEmail = "noreply@example.test", FromName = "Nexus" });
        return new(client, monitor.Object, NullLogger<ResendEmailDeliverySender>.Instance);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
