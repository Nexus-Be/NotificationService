namespace Notification.Infrastructure.Services.Email;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notification.Application.Interfaces;
using Notification.Application.Services.Deliveries;

/// <summary>Transactional email sender using Brevo HTTPS API v3.</summary>
public sealed class BrevoEmailDeliverySender : IEmailDeliverySender
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<BrevoOptions> _options;
    private readonly ILogger<BrevoEmailDeliverySender> _logger;

    public BrevoEmailDeliverySender(
        HttpClient httpClient,
        IOptionsMonitor<BrevoOptions> options,
        ILogger<BrevoEmailDeliverySender> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<EmailDeliveryResult> SendAsync(EmailDeliveryMessage request, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.SenderEmail))
        {
            return EmailDeliveryResult.Failure("BREVO_NOT_CONFIGURED", "Brevo API key or sender email is not configured.");
        }

        string normalizedRecipient;
        string normalizedSender;
        try
        {
            normalizedRecipient = new MailAddress(request.Recipient.Trim()).Address;
            normalizedSender = new MailAddress(options.SenderEmail.Trim()).Address;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return EmailDeliveryResult.Failure("EMAIL_INVALID", "Email message or address is invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.HtmlBody))
        {
            return EmailDeliveryResult.Failure("EMAIL_INVALID", "Email subject and HTML content are required.");
        }

        var endpoint = BuildEndpointUri(options.BaseUrl);
        if (endpoint is null)
        {
            return EmailDeliveryResult.Failure("BREVO_NOT_CONFIGURED", "Brevo API base URL is invalid.");
        }

        var senderName = string.IsNullOrWhiteSpace(options.SenderName) ? "Nexus" : options.SenderName.Trim();
        var payload = new BrevoSendEmailPayload(
            new BrevoSender(senderName, normalizedSender),
            new[] { new BrevoRecipient(normalizedRecipient) },
            request.Subject,
            request.HtmlBody,
            new Dictionary<string, string>
            {
                ["Message-Id"] = $"<{request.MessageId}>"
            });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        httpRequest.Headers.TryAddWithoutValidation("api-key", options.ApiKey.Trim());
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return EmailDeliveryResult.Sent();
            }

            _logger.LogWarning(
                "Brevo rejected email delivery {DeliveryId} with HTTP status {StatusCode}.",
                request.DeliveryId,
                (int)response.StatusCode);

            return ClassifyStatusCode(response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or TimeoutException)
        {
            _logger.LogWarning(ex, "Brevo HTTP request failed for delivery {DeliveryId}.", request.DeliveryId);
            return EmailDeliveryResult.Failure("BREVO_TEMPORARY_ERROR", "Brevo API connection failed temporarily.", true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Brevo request failed unexpectedly for delivery {DeliveryId}.", request.DeliveryId);
            return EmailDeliveryResult.Failure("BREVO_UNEXPECTED_ERROR", "Brevo API request failed unexpectedly.", true);
        }
    }

    public static EmailDeliveryResult ClassifyStatusCode(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                EmailDeliveryResult.Failure("BREVO_AUTH_FAILED", "Brevo API authentication failed."),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
                EmailDeliveryResult.Failure("EMAIL_INVALID", "Brevo rejected the email request."),
            HttpStatusCode.TooManyRequests =>
                EmailDeliveryResult.Failure("BREVO_RATE_LIMITED", "Brevo rate limit exceeded.", true),
            _ when code >= 500 =>
                EmailDeliveryResult.Failure($"BREVO_{code}", "Brevo server returned an unexpected error.", true),
            _ =>
                EmailDeliveryResult.Failure($"BREVO_{code}", "Brevo rejected the email request.")
        };
    }

    private static Uri? BuildEndpointUri(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root))
        {
            return null;
        }

        return new Uri(root, "smtp/email");
    }

    private sealed record BrevoSendEmailPayload(
        [property: JsonPropertyName("sender")] BrevoSender Sender,
        [property: JsonPropertyName("to")] IReadOnlyList<BrevoRecipient> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("htmlContent")] string HtmlContent,
        [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string> Headers);

    private sealed record BrevoSender(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("email")] string Email);

    private sealed record BrevoRecipient(
        [property: JsonPropertyName("email")] string Email);
}
