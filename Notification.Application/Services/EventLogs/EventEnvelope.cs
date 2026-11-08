namespace Notification.Application.Services.EventLogs;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Notification.Domain.Exceptions;

internal static class EventEnvelope
{
    internal static DomainException Invalid() => new("VAL_INVALID_INPUT", "Event envelope or query parameters are invalid.", 400);
    internal static string Source(string value)
    {
        var source = value.Trim().ToLowerInvariant();
        if (source.Length is < 1 or > 100 || !Regex.IsMatch(source, @"^[a-z][a-z0-9-]*$")) throw Invalid();
        return source;
    }
    internal static string Type(string value)
    {
        var type = value.Trim();
        if (type.Length is < 1 or > 150 || !Regex.IsMatch(type, @"^[A-Za-z][A-Za-z0-9._:-]*$")) throw Invalid();
        return type;
    }
    internal static string? Text(string? value, int max)
    {
        if (value is null) return null;
        if (value.Any(char.IsControl)) throw Invalid();
        var text = value.Trim();
        if (text.Length is < 1 || text.Length > max || text.Any(char.IsControl) || !ValidUnicode(text)) throw Invalid();
        return text;
    }
    private static bool ValidUnicode(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (++i >= value.Length || !char.IsLowSurrogate(value[i])) return false;
            }
            else if (char.IsLowSurrogate(value[i])) return false;
        }
        return true;
    }
    internal static string CanonicalPayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) throw Invalid();
        try
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) Write(payload, writer, 0);
            if (stream.Length > 65536) throw Invalid();
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            throw Invalid();
        }
    }
    private static void Write(JsonElement value, Utf8JsonWriter writer, int depth)
    {
        if (depth > 32) throw Invalid();
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!names.Add(p.Name) || p.Name.Contains('\0') || !ValidUnicode(p.Name)) throw Invalid();
                    writer.WritePropertyName(p.Name); Write(p.Value, writer, depth + 1);
                }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) Write(item, writer, depth + 1);
                writer.WriteEndArray(); break;
            case JsonValueKind.String:
                var text = value.GetString()!;
                if (text.Contains('\0') || !ValidUnicode(text)) throw Invalid();
                writer.WriteStringValue(text); break;
            case JsonValueKind.Number:
                var number = value.GetRawText();
                var exponent = number.IndexOfAny(['e', 'E']);
                if (number.Length > 256 || !value.TryGetDouble(out var d) || !double.IsFinite(d) ||
                    (exponent >= 0 && (!int.TryParse(number[(exponent + 1)..], NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out var e) || e is < -300 or > 300))) throw Invalid();
                writer.WriteRawValue(number); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw Invalid();
        }
    }
    internal static string Headers(Dictionary<string, string>? headers)
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (headers is not null)
        {
            if (headers.Count > 3) throw Invalid();
            foreach (var (key, value) in headers)
            {
                var normalized = key.ToLowerInvariant();
                if (normalized is not ("x-request-id" or "x-correlation-id" or "traceparent") ||
                    value is null || !sorted.TryAdd(normalized, Text(value, 256)!)) throw Invalid();
            }
        }
        return JsonSerializer.Serialize(sorted);
    }
}
