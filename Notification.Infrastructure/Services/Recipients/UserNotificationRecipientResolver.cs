namespace Notification.Infrastructure.Services.Recipients;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Notification.Application.Interfaces;
public sealed class UserNotificationRecipientResolver(HttpClient http, IConfiguration config) : INotificationRecipientResolver
{
    public async Task<string?> ResolveAsync(Guid userId, CancellationToken ct = default)
    {
        var key = config["Services:User:NotificationContactKey"] ?? config["INTERNAL_SERVICE_SECRET"] ?? config["InternalSecurity:ServiceKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new HttpRequestException("Recipient service authentication is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/users/{userId}/notification-contacts");
        request.Headers.Add("X-Notification-Contact-Key", key);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var contact = await response.Content.ReadFromJsonAsync<Contact>(cancellationToken: ct);
        if (contact?.UserId != userId || string.IsNullOrWhiteSpace(contact.Email) || contact.Email.Length > 254 ||
            !new EmailAddressAttribute().IsValid(contact.Email)) throw new HttpRequestException("Recipient service returned invalid contact data.");
        return contact.Email;
    }
    private sealed record Contact(Guid UserId, string Email);
}
