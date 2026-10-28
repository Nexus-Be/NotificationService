
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Notification.Api.Controllers;

internal static class EventLogPayloadSanitizer
{
    public static JsonObject? Sanitize(
        string eventType,
        string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        // Only explicitly approved event types
        // can expose selected payload fields.
        if (!string.Equals(
            eventType,
            "OrderPaid",
            StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);

            if (document.RootElement.ValueKind
                != JsonValueKind.Object)
                return null;

            var source = document.RootElement;
            var safe = new JsonObject();

            // Only allow a valid order UUID
            if (source.TryGetProperty("orderId", out var id)
                && id.ValueKind == JsonValueKind.String
                && Guid.TryParse(id.GetString(), out var orderId))
            {
                safe["orderId"] = orderId.ToString();
            }

            // Only allow a valid non-negative amount
            if (source.TryGetProperty("totalAmount", out var total)
                && total.ValueKind == JsonValueKind.Number
                && total.TryGetDecimal(out var amount)
                && amount >= 0)
            {
                safe["totalAmount"] = amount;
            }

            return safe.Count > 0 ? safe : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
