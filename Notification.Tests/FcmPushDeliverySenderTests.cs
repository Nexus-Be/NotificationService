namespace Notification.Tests;

using System.Net;
using Google.Apis.Auth.OAuth2.Responses;
using System.Net.Http.Headers;
using System.Text.Json;
using Notification.Application.Services.Deliveries;
using Notification.Infrastructure.Services.Push;
using Xunit;

public sealed class FcmPushDeliverySenderTests
{
    private static PushDeliveryMessage Message() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "private-fcm-token", "Order paid", "Body");

    [Fact]
    public async Task Send_UsesHttpV1OAuthAndStableIds_WithOneRequest()
    {
        var message = Message();
        var handler = new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://fcm.googleapis.com/v1/projects/project-test/messages:send", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-oauth-token", request.Headers.Authorization.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var json = body.RootElement.GetProperty("message");
            Assert.Equal(message.Token, json.GetProperty("token").GetString());
            Assert.Equal(message.Title, json.GetProperty("notification").GetProperty("title").GetString());
            Assert.Equal(message.DeliveryId.ToString(), json.GetProperty("data").GetProperty("deliveryId").GetString());
            Assert.Equal(message.UserId.ToString(), json.GetProperty("data").GetProperty("userId").GetString());
            return Json(HttpStatusCode.OK, "{\"name\":\"projects/project-test/messages/accepted\"}");
        });
        using var client = new HttpClient(handler);
        var result = await new FcmPushDeliverySender(client, new Tokens(), new FcmOptions { ProjectId = "project-test" }, TimeProvider.System).SendAsync(message);
        Assert.True(result.Succeeded);
        Assert.Equal("projects/project-test/messages/accepted", result.ProviderMessageId);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(404, "UNREGISTERED", false, true, "FCM_UNREGISTERED")]
    [InlineData(400, "INVALID_ARGUMENT", false, false, "FCM_INVALID_ARGUMENT")]
    [InlineData(403, "SENDER_ID_MISMATCH", false, false, "FCM_SENDER_ID_MISMATCH")]
    [InlineData(401, "THIRD_PARTY_AUTH_ERROR", false, false, "FCM_THIRD_PARTY_AUTH_ERROR")]
    [InlineData(503, "UNAVAILABLE", true, false, "FCM_UNAVAILABLE")]
    [InlineData(500, "INTERNAL", true, false, "FCM_UNAVAILABLE")]
    [InlineData(429, "QUOTA_EXCEEDED", true, false, "FCM_QUOTA_EXCEEDED")]
    public async Task ErrorDetails_AreClassifiedWithoutRawTokenLeak(int status, string code, bool transient, bool invalidate, string expected)
    {
        var handler = new Handler(_ => Task.FromResult(Json((HttpStatusCode)status, JsonSerializer.Serialize(new
        {
            error = new { message = "private-fcm-token", details = new[] { new Dictionary<string,string> { ["@type"] = "type.googleapis.com/google.firebase.fcm.v1.FcmError", ["errorCode"] = code } } }
        }))));
        using var client = new HttpClient(handler);
        var result = await new FcmPushDeliverySender(client, new Tokens(), new FcmOptions { ProjectId = "project-test" }, TimeProvider.System).SendAsync(Message());
        Assert.Equal(expected, result.ErrorCode);
        Assert.Equal(transient, result.IsTransient);
        Assert.Equal(invalidate, result.InvalidateToken);
        Assert.DoesNotContain("private-fcm-token", result.ErrorMessage!);
        if (status == 429) Assert.True(result.RetryAfter >= TimeSpan.FromSeconds(60));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RetryAfter_IsHonoredAndQuotaHasMinimumDelay()
    {
        var handler = new Handler(_ =>
        {
            var response = Json(HttpStatusCode.ServiceUnavailable, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var result = await new FcmPushDeliverySender(client, new Tokens(), new FcmOptions { ProjectId = "project-test" }, TimeProvider.System).SendAsync(Message());
        Assert.Equal(TimeSpan.FromSeconds(120), result.RetryAfter);
        Assert.Equal(TimeSpan.FromSeconds(60), FcmPushDeliverySender.ClassifyFailure(429, null, TimeSpan.FromSeconds(1)).RetryAfter);
    }

    [Fact]
    public async Task RetryAfterHttpDate_IsParsed()
    {
        var clock = new Clock();
        var handler = new Handler(_ =>
        {
            var response = Json(HttpStatusCode.ServiceUnavailable, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(clock.GetUtcNow().AddSeconds(90));
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var result = await new FcmPushDeliverySender(client, new Tokens(), new FcmOptions { ProjectId = "project-test" }, clock).SendAsync(Message());
        Assert.Equal(TimeSpan.FromSeconds(90), result.RetryAfter);
    }

    [Theory]
    [InlineData(503, "not-json", true)]
    [InlineData(404, "[]", false)]
    [InlineData(401, "{}", false)]
    [InlineData(200, "[]", true)]
    public async Task MalformedResponses_DoNotInvalidateTokens(int status, string body, bool transient)
    {
        using var client = new HttpClient(new Handler(_ => Task.FromResult(Json((HttpStatusCode)status, body))));
        var result = await new FcmPushDeliverySender(client, new Tokens(), new FcmOptions { ProjectId = "project-test" }, TimeProvider.System).SendAsync(Message());
        Assert.False(result.Succeeded);
        Assert.Equal(transient, result.IsTransient);
        Assert.False(result.InvalidateToken);
    }

    [Fact]
    public async Task MissingConfigurationOrCredentials_NeverSimulatesSuccess()
    {
        var handler = new Handler(_ => throw new InvalidOperationException("FCM must not be contacted."));
        using var client = new HttpClient(handler);
        var tokens = new Tokens();
        var missing = await new FcmPushDeliverySender(client, tokens, new FcmOptions(), TimeProvider.System).SendAsync(Message());
        Assert.Equal("FCM_NOT_CONFIGURED", missing.ErrorCode);
        Assert.Equal(0, tokens.Calls);
        var invalid = await new FcmPushDeliverySender(client, new Tokens { Throw = true }, new FcmOptions { ProjectId = "project-test" }, TimeProvider.System).SendAsync(Message());
        Assert.Equal("FCM_AUTH_FAILED", invalid.ErrorCode);
        Assert.False(invalid.IsTransient);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Cancellation_PropagatesToWorker()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var client = new HttpClient(new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK, "{}"))));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FcmPushDeliverySender(client, new Tokens(), new FcmOptions { ProjectId = "project-test" }, TimeProvider.System).SendAsync(Message(), cancellation.Token));
    }

    [Theory]
    [InlineData(503, true)]
    [InlineData(429, true)]
    [InlineData(400, false)]
    public async Task OAuthServiceErrors_DistinguishTemporaryOutageFromInvalidCredentials(int status, bool transient)
    {
        var handler = new Handler(_ => throw new InvalidOperationException("FCM must not be contacted before OAuth succeeds."));
        using var client = new HttpClient(handler);
        var tokens = new Tokens { Failure = new TokenResponseException(new TokenErrorResponse { Error = transient ? "server_error" : "invalid_grant" }, (HttpStatusCode)status) };
        var result = await new FcmPushDeliverySender(client, tokens, new FcmOptions { ProjectId = "project-test" }, TimeProvider.System).SendAsync(Message());
        Assert.Equal(transient, result.IsTransient);
        Assert.Equal(0, handler.Calls);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };
    private sealed class Tokens : IFcmAccessTokenProvider
    {
        public int Calls { get; private set; }
        public bool Throw { get; init; }
        public Exception? Failure { get; init; }
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (Failure is not null) throw Failure;
            if (Throw) throw new InvalidOperationException("Invalid credentials.");
            return Task.FromResult("test-oauth-token");
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return action(request);
        }
    }
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    }
}
