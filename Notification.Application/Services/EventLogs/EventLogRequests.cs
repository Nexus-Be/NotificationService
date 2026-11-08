namespace Notification.Application.Services.EventLogs;

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record CreateEventLogRequest
{
    [Required(ErrorMessage = "Event ID is required.")] public Guid? EventId { get; init; }
    [Required(ErrorMessage = "Source service is required."), StringLength(100)] public string SourceService { get; init; } = string.Empty;
    [Required(ErrorMessage = "Event type is required."), StringLength(150)] public string EventType { get; init; } = string.Empty;
    [Required(ErrorMessage = "Occurrence time is required.")] public DateTimeOffset? OccurredAt { get; init; }
    public Guid? ActorId { get; init; }
    [StringLength(50)] public string? ActorRole { get; init; }
    public Guid? TargetId { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
    [JsonConverter(typeof(EventHeadersConverter))]
    public Dictionary<string, string>? Headers { get; init; }
    public JsonElement Payload { get; init; }
}

// Preserve duplicate-key validation before the dictionary model binder could discard a value.
public sealed class EventHeadersConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Headers must be a JSON object.");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Header name is invalid.");
            var key = reader.GetString()!;
            if (!reader.Read() || reader.TokenType != JsonTokenType.String || !result.TryAdd(key, reader.GetString()!) || result.Count > 3)
                throw new JsonException("Headers contain invalid or duplicate fields.");
        }
        if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException("Headers object is incomplete.");
        return result;
    }
    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var item in value) writer.WriteString(item.Key, item.Value);
        writer.WriteEndObject();
    }
}

public sealed record ListEventLogsRequest
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(100)] public string? SourceService { get; init; }
    [StringLength(150)] public string? EventType { get; init; }
    public Guid? ActorId { get; init; }
    public Guid? TargetId { get; init; }
    public Guid? EventId { get; init; }
    [StringLength(100)] public string? TraceId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}
