namespace Notification.Tests;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Notification.Application.Interfaces;
using Notification.Application.Services.Deliveries;
using Notification.Infrastructure;
using Notification.Infrastructure.Services.Email;
using Xunit;

public sealed class BrevoEmailDeliverySenderTests
{
    private static BrevoEmailDeliverySender CreateSender(
        BrevoOptions options,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var monitor = new Mock<IOptionsMonitor<BrevoOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(options);
        var client = new HttpClient(new StubHandler(handler));
        return new BrevoEmailDeliverySender(client, monitor.Object, NullLogger<BrevoEmailDeliverySender>.Instance);
    }

    [Fact]
    public async Task MissingApiKeyOrSender_NeverSimulatesSuccess()
    {
        var sender = CreateSender(new BrevoOptions(), (_, _) =>
            throw new InvalidOperationException("HTTP must not be called when unconfigured."));

        var result = await sender.SendAsync(new EmailDeliveryMessage(Guid.NewGuid(), "user@example.test", "Hello", "<p>Body</p>"));

        Assert.False(result.Succeeded);
        Assert.False(result.IsTransient);
        Assert.Equal("BREVO_NOT_CONFIGURED", result.ErrorCode);
    }

    [Fact]
    public async Task InvalidRecipientOrSender_ReturnsEmailInvalidWithoutCallingHttp()
    {
        var sender = CreateSender(new BrevoOptions
        {
            ApiKey = "xkeysib-test",
            SenderEmail = "phamquochoang356@gmail.com"
        }, (_, _) => throw new InvalidOperationException("HTTP must not be called for invalid addresses."));

        var result = await sender.SendAsync(new EmailDeliveryMessage(Guid.NewGuid(), "not-an-email", "Hello", "<p>Body</p>"));

        Assert.False(result.Succeeded);
        Assert.False(result.IsTransient);
        Assert.Equal("EMAIL_INVALID", result.ErrorCode);
    }

    [Fact]
    public async Task CreatedResponse_SendsExpectedHeadersAndBrevoPayload()
    {
        Uri? requestUri = null;
        string? apiKeyHeader = null;
        string? requestBody = null;

        var sender = CreateSender(new BrevoOptions
        {
            BaseUrl = "https://api.brevo.com/v3",
            ApiKey = "xkeysib-test-secret",
            SenderName = "Nexus",
            SenderEmail = "phamquochoang356@gmail.com"
        }, async (request, ct) =>
        {
            requestUri = request.RequestUri;
            apiKeyHeader = request.Headers.GetValues("api-key").Single();
            requestBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"messageId\":\"<msg-1@brevo>\"}", Encoding.UTF8, "application/json")
            };
        });

        var message = new EmailDeliveryMessage(Guid.NewGuid(), "recipient@example.test", "OTP Code", "<p>123456</p>");
        var result = await sender.SendAsync(message);

        Assert.True(result.Succeeded);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", requestUri?.ToString());
        Assert.Equal("xkeysib-test-secret", apiKeyHeader);

        using var json = JsonDocument.Parse(requestBody!);
        var root = json.RootElement;
        Assert.Equal("Nexus", root.GetProperty("sender").GetProperty("name").GetString());
        Assert.Equal("phamquochoang356@gmail.com", root.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal("recipient@example.test", root.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("OTP Code", root.GetProperty("subject").GetString());
        Assert.Equal("<p>123456</p>", root.GetProperty("htmlContent").GetString());
        Assert.Equal($"<{message.MessageId}>", root.GetProperty("headers").GetProperty("Message-Id").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "BREVO_AUTH_FAILED", false)]
    [InlineData(HttpStatusCode.Forbidden, "BREVO_AUTH_FAILED", false)]
    [InlineData(HttpStatusCode.BadRequest, "EMAIL_INVALID", false)]
    [InlineData(HttpStatusCode.UnprocessableEntity, "EMAIL_INVALID", false)]
    [InlineData(HttpStatusCode.TooManyRequests, "BREVO_RATE_LIMITED", true)]
    [InlineData(HttpStatusCode.InternalServerError, "BREVO_500", true)]
    [InlineData(HttpStatusCode.BadGateway, "BREVO_502", true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "BREVO_503", true)]
    public async Task ErrorResponses_ClassifyWithoutLeakingRawProviderBody(
        HttpStatusCode status,
        string expectedCode,
        bool expectedTransient)
    {
        var sender = CreateSender(new BrevoOptions
        {
            ApiKey = "xkeysib-test-secret",
            SenderEmail = "phamquochoang356@gmail.com"
        }, (_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"message\":\"sensitive-raw-provider-error\"}", Encoding.UTF8, "application/json")
        }));

        var result = await sender.SendAsync(new EmailDeliveryMessage(Guid.NewGuid(), "user@example.test", "Hello", "<p>Body</p>"));

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Equal(expectedTransient, result.IsTransient);
        Assert.DoesNotContain("sensitive-raw", result.ErrorMessage!);
        Assert.DoesNotContain("xkeysib", result.ErrorMessage!);
    }

    [Fact]
    public async Task NetworkAndTimeoutExceptions_AreTransient()
    {
        var networkSender = CreateSender(new BrevoOptions
        {
            ApiKey = "xkeysib-test",
            SenderEmail = "phamquochoang356@gmail.com"
        }, (_, _) => throw new HttpRequestException("Network failure"));

        var networkResult = await networkSender.SendAsync(new EmailDeliveryMessage(Guid.NewGuid(), "user@example.test", "Hello", "<p>Body</p>"));
        Assert.False(networkResult.Succeeded);
        Assert.True(networkResult.IsTransient);
        Assert.Equal("BREVO_TEMPORARY_ERROR", networkResult.ErrorCode);

        var timeoutSender = CreateSender(new BrevoOptions
        {
            ApiKey = "xkeysib-test",
            SenderEmail = "phamquochoang356@gmail.com"
        }, (_, _) => throw new TaskCanceledException("HttpClient internal timeout"));

        var timeoutResult = await timeoutSender.SendAsync(new EmailDeliveryMessage(Guid.NewGuid(), "user@example.test", "Hello", "<p>Body</p>"));
        Assert.False(timeoutResult.Succeeded);
        Assert.True(timeoutResult.IsTransient);
        Assert.Equal("BREVO_TEMPORARY_ERROR", timeoutResult.ErrorCode);
    }

    [Fact]
    public async Task BrevoEmailSenderService_RendersPurposeHtmlAndUsesDeliverySender()
    {
        EmailDeliveryMessage? captured = null;
        var deliverySender = CreateSender(new BrevoOptions
        {
            ApiKey = "xkeysib-test",
            SenderName = "Nexus",
            SenderEmail = "phamquochoang356@gmail.com"
        }, async (request, ct) =>
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            captured = new EmailDeliveryMessage(
                Guid.NewGuid(),
                doc.RootElement.GetProperty("to")[0].GetProperty("email").GetString()!,
                doc.RootElement.GetProperty("subject").GetString()!,
                doc.RootElement.GetProperty("htmlContent").GetString()!);
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        var emailService = new BrevoEmailSenderService(deliverySender, NullLogger<BrevoEmailSenderService>.Instance);
        var sent = await emailService.SendOtpEmailAsync("user@example.test", "<b>Hoang</b>", "654321", 10, "password_reset");

        Assert.True(sent);
        Assert.NotNull(captured);
        Assert.Equal("user@example.test", captured!.Recipient);
        Assert.Contains("Password Reset", captured.Subject);
        Assert.Contains("&lt;b&gt;Hoang&lt;/b&gt;", captured.HtmlBody);
        Assert.Contains("654321", captured.HtmlBody);
        Assert.Contains("10 minutes", captured.HtmlBody);
    }

    [Fact]
    public void DependencyInjection_SelectsBrevoOrSmtpBasedOnConfiguration()
    {
        var brevoConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5439;Database=notification_db;Username=postgres;Password=postgres",
                ["EMAIL_PROVIDER"] = "Brevo",
                ["BREVO_API_KEY"] = "xkeysib-test",
                ["BREVO_SENDER_EMAIL"] = "phamquochoang356@gmail.com",
                ["BREVO_SENDER_NAME"] = "Nexus"
            })
            .Build();

        var brevoServices = new ServiceCollection();
        brevoServices.AddLogging();
        brevoServices.AddInfrastructureServices(brevoConfig);
        using var brevoProvider = brevoServices.BuildServiceProvider();

        Assert.IsType<BrevoEmailDeliverySender>(brevoProvider.GetRequiredService<IEmailDeliverySender>());
        Assert.IsType<BrevoEmailSenderService>(brevoProvider.GetRequiredService<IEmailSenderService>());
        Assert.IsType<BrevoEmailSenderService>(brevoProvider.GetRequiredService<IEmailService>());

        var options = brevoProvider.GetRequiredService<IOptionsMonitor<BrevoOptions>>().CurrentValue;
        Assert.Equal("xkeysib-test", options.ApiKey);
        Assert.Equal("phamquochoang356@gmail.com", options.SenderEmail);
        Assert.Equal("Nexus", options.SenderName);

        var smtpConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5439;Database=notification_db;Username=postgres;Password=postgres"
            })
            .Build();

        var smtpServices = new ServiceCollection();
        smtpServices.AddLogging();
        smtpServices.AddInfrastructureServices(smtpConfig);
        using var smtpProvider = smtpServices.BuildServiceProvider();

        Assert.IsType<SmtpEmailDeliverySender>(smtpProvider.GetRequiredService<IEmailDeliverySender>());
        Assert.IsType<MailKitEmailSenderService>(smtpProvider.GetRequiredService<IEmailSenderService>());
    }

    [Fact]
    public void DependencyInjection_RejectsUnknownEmailProvider()
    {
        var invalidConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5439;Database=notification_db;Username=postgres;Password=postgres",
                ["EMAIL_PROVIDER"] = "UnknownProvider"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructureServices(invalidConfig));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
