namespace Notification.Infrastructure.Services.Email;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notification.Application.Interfaces;
using Notification.Application.Services.Deliveries;

/// <summary>Sends transactional mail over HTTPS. The delivery ID is reused on retries.</summary>
public sealed class ResendEmailDeliverySender : IEmailDeliverySender
{
    private readonly HttpClient _client;
    private readonly IOptionsMonitor<ResendOptions> _options;
    private readonly ILogger<ResendEmailDeliverySender> _logger;

    public ResendEmailDeliverySender(HttpClient client, IOptionsMonitor<ResendOptions> options,
        ILogger<ResendEmailDeliverySender> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<EmailDeliveryResult> SendAsync(EmailDeliveryMessage message,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.FromEmail))
            return EmailDeliveryResult.Failure("RESEND_NOT_CONFIGURED", "Email provider is not configured.");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            request.Headers.Add("Idempotency-Key", $"nexus-delivery-{message.DeliveryId:N}");
            var from = string.IsNullOrWhiteSpace(options.FromName)
                ? options.FromEmail
                : $"{options.FromName.Trim()} <{options.FromEmail.Trim()}>";
            request.Content = JsonContent.Create(new
            {
                from,
                to = new[] { message.Recipient },
                subject = message.Subject,
                html = message.HtmlBody
            });
            using var response = await _client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var body = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (body.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(id.GetString()))
                    return EmailDeliveryResult.Sent(id.GetString());
                return EmailDeliveryResult.Failure("RESEND_INVALID_RESPONSE", "Email provider response was invalid.", true);
            }

            var transient = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                            (int)response.StatusCode >= 500;
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                transient = errorBody.Contains("concurrent_idempotent_requests", StringComparison.Ordinal);
            }
            _logger.LogWarning("Resend rejected delivery {DeliveryId} with HTTP {StatusCode}.",
                message.DeliveryId, (int)response.StatusCode);
            return EmailDeliveryResult.Failure($"RESEND_HTTP_{(int)response.StatusCode}",
                "Email provider rejected the request.", transient);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Resend connection failed for delivery {DeliveryId}.", message.DeliveryId);
            return EmailDeliveryResult.Failure("RESEND_CONNECTION_ERROR", "Email provider connection failed.", true);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Resend returned invalid JSON for delivery {DeliveryId}.", message.DeliveryId);
            return EmailDeliveryResult.Failure("RESEND_INVALID_RESPONSE", "Email provider response was invalid.", true);
        }
    }
}
