namespace Notification.Infrastructure.Services.Push;

using System.Net.Http.Headers;
using Google.Apis.Auth.OAuth2.Responses;
using System.Net.Http.Json;
using System.Text.Json;
using Notification.Application.Interfaces;
using Notification.Application.Services.Deliveries;

public sealed class FcmPushDeliverySender : IPushDeliverySender
{
    private readonly HttpClient _http;
    private readonly IFcmAccessTokenProvider _tokens;
    private readonly FcmOptions _options;
    private readonly TimeProvider _time;

    public FcmPushDeliverySender(HttpClient http, IFcmAccessTokenProvider tokens, FcmOptions options, TimeProvider time)
    {
        _http = http;
        _tokens = tokens;
        _options = options;
        _time = time;
    }

    public async Task<PushDeliveryResult> SendAsync(PushDeliveryMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ProjectId))
            return PushDeliveryResult.Failure("FCM_NOT_CONFIGURED", "FCM project ID is not configured.");
        if (string.IsNullOrWhiteSpace(message.Token))
            return PushDeliveryResult.Failure("FCM_INVALID_TOKEN", "FCM registration token is empty.");
        string accessToken;
        try { accessToken = await _tokens.GetAccessTokenAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TokenResponseException ex) when ((int?)ex.StatusCode >= 500 || (int?)ex.StatusCode == 429 || ex.Error?.Error is "temporarily_unavailable" or "server_error")
        { return PushDeliveryResult.Failure("FCM_AUTH_TEMPORARY", "FCM credential service is temporarily unavailable.", true); }
        catch (HttpRequestException) { return PushDeliveryResult.Failure("FCM_AUTH_TEMPORARY", "FCM credential service is temporarily unavailable.", true); }
        catch (Exception) { return PushDeliveryResult.Failure("FCM_AUTH_FAILED", "FCM server credentials are unavailable or invalid."); }
        if (string.IsNullOrWhiteSpace(accessToken))
            return PushDeliveryResult.Failure("FCM_AUTH_FAILED", "FCM access token is unavailable.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://fcm.googleapis.com/v1/projects/" + Uri.EscapeDataString(_options.ProjectId.Trim()) + "/messages:send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                token = message.Token,
                notification = new { title = message.Title, body = message.Body },
                data = new { notificationId = message.NotificationId.ToString(), deliveryId = message.DeliveryId.ToString(), userId = message.UserId.ToString() }
            }
        });
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            JsonDocument? json = null;
            try
            {
                await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
                json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
            }
            catch (JsonException) { /* Use the HTTP status when an error body is not JSON. */ }
            using (json)
            {
                if (response.IsSuccessStatusCode)
                {
                    if (json is not null && json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
                        return PushDeliveryResult.Sent(name.GetString()!);
                    return PushDeliveryResult.Failure("FCM_INVALID_RESPONSE", "FCM returned an invalid acceptance response.", true);
                }
                var remoteCode = ReadRemoteCode(json);
                var wait = response.Headers.RetryAfter?.Delta;
                if (wait is null && response.Headers.RetryAfter?.Date is { } date)
                    wait = date - _time.GetUtcNow();
                if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
                return ClassifyFailure((int)response.StatusCode, remoteCode, wait);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException) { return PushDeliveryResult.Failure("FCM_NETWORK_ERROR", "FCM request failed temporarily.", true); }
    }

    private static string? ReadRemoteCode(JsonDocument? json)
    {
        if (json is null || json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object ||
            !error.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in details.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("@type", out var type) &&
                type.ValueKind == JsonValueKind.String && type.GetString() == "type.googleapis.com/google.firebase.fcm.v1.FcmError" &&
                item.TryGetProperty("errorCode", out var code) && code.ValueKind == JsonValueKind.String)
                return code.GetString();
        return null;
    }

    public static PushDeliveryResult ClassifyFailure(int status, string? code, TimeSpan? retryAfter = null)
    {
        if (code == "UNREGISTERED")
            return PushDeliveryResult.Failure("FCM_UNREGISTERED", "FCM registration token is no longer registered.", invalidateToken: true);
        if (code == "INVALID_ARGUMENT")
            return PushDeliveryResult.Failure("FCM_INVALID_ARGUMENT", "FCM message or registration token is invalid.");
        if (code is "SENDER_ID_MISMATCH" or "THIRD_PARTY_AUTH_ERROR")
            return PushDeliveryResult.Failure("FCM_" + code, "FCM sender or platform credentials do not match the device.");
        if (status == 429 || code == "QUOTA_EXCEEDED")
            return PushDeliveryResult.Failure("FCM_QUOTA_EXCEEDED", "FCM quota was exceeded.", true,
                retryAfter: retryAfter > TimeSpan.FromSeconds(60) ? retryAfter : TimeSpan.FromSeconds(60));
        if (status >= 500 || code is "UNAVAILABLE" or "INTERNAL")
            return PushDeliveryResult.Failure("FCM_UNAVAILABLE", "FCM service is temporarily unavailable.", true, retryAfter: retryAfter);
        if (status is 401 or 403)
            return PushDeliveryResult.Failure("FCM_AUTH_FAILED", "FCM request was not authorized.");
        return PushDeliveryResult.Failure("FCM_HTTP_" + status, "FCM rejected the request.");
    }
}
