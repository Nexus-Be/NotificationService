
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;

namespace Notification.Application.Services.Notifications;

public sealed class NotificationChannelService
    : INotificationChannelService
{
    private readonly INotificationDbContext _db;

    public NotificationChannelService(
        INotificationDbContext db)
    {
        _db = db;
    }

    // NOTI-07: Get channel configuration
    public async Task<NotificationChannelDto?> GetAsync(
        ChannelType code,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(code))
            throw new ArgumentException("Invalid channel code.");

        var channel = await _db.NotificationChannels
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Code == code && !c.IsDeleted,
                cancellationToken);

        if (channel is null)
            return null;

        return ToDto(channel);
    }

    // NOTI-07: Update channel configuration
    public async Task<NotificationChannelDto?> UpdateAsync(
        ChannelType code,
        UpdateNotificationChannelDto request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(code))
            throw new ArgumentException("Invalid channel code.");

        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name)
            || request.Name.Length > 150)
        {
            throw new ArgumentException(
                "Channel name must be between 1 and 150 characters.");
        }

        if (request.Provider.HasValue
            && !Enum.IsDefined(request.Provider.Value))
        {
            throw new ArgumentException(
                "Invalid channel provider.");
        }

        // Validate JSON and reject sensitive fields
        string? safeConfig = null;

        if (request.ConfigJson is not null)
        {
            safeConfig = ValidateConfig(request.ConfigJson);
        }

        var channel = await _db.NotificationChannels
            .AsTracking()
            .SingleOrDefaultAsync(
                c => c.Code == code && !c.IsDeleted,
                cancellationToken);

        if (channel is null)
            return null;

        // Update channel properties
        channel.Name = request.Name.Trim();
        channel.Provider = request.Provider;
        channel.Config = safeConfig;
        channel.IsDisabled = !request.IsEnabled;
        channel.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(channel);
    }

    // Convert database entity into response DTO
    private static NotificationChannelDto ToDto(
        NotificationChannel channel)
    {
        return new NotificationChannelDto(
            channel.Id,
            channel.Code,
            channel.Name,
            channel.Provider,
            SanitizeConfig(channel.Config),
            !channel.IsDisabled,
            channel.UpdatedAt
        );
    }

    // Validate configuration before saving
    private static string ValidateConfig(string config)
    {
        if (string.IsNullOrWhiteSpace(config)
            || config.Length > 8192)
        {
            throw new ArgumentException(
                "Invalid channel configuration.");
        }

        JsonNode? node;

        try
        {
            node = JsonNode.Parse(config);
        }
        catch (JsonException)
        {
            throw new ArgumentException(
                "Configuration must be valid JSON.");
        }

        if (node is not JsonObject)
        {
            throw new ArgumentException(
                "Configuration must be a JSON object.");
        }

        if (ContainsSensitiveFields(node))
        {
            throw new InvalidOperationException(
                "SECRET_IN_CONFIG_FORBIDDEN");
        }

        return node.ToJsonString();
    }

    // Detect password, secret, API key, token, etc.
    private static bool ContainsSensitiveFields(
        JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var item in obj)
            {
                if (IsSensitiveField(item.Key)
                    || ContainsSensitiveFields(item.Value))
                {
                    return true;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (ContainsSensitiveFields(item))
                    return true;
            }
        }

        return false;
    }

    private static bool IsSensitiveField(string name)
    {
        var normalized = new string(
            name.Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

        string[] forbidden =
        {
            "password",
            "secret",
            "token",
            "key",
            "credential",
            "authorization",
            "connectionstring",
            "bearer",
            "certificate"
        };

        return forbidden.Any(normalized.Contains);
    }

    // Remove sensitive fields from existing DB configuration
    private static string? SanitizeConfig(string? config)
    {
        if (string.IsNullOrWhiteSpace(config))
            return null;

        try
        {
            var node = JsonNode.Parse(config);

            if (node is not JsonObject)
                return null;

            RemoveSensitiveFields(node);

            return node.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void RemoveSensitiveFields(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj
                .Select(item => item.Key).ToList())
            {
                if (IsSensitiveField(key))
                    obj.Remove(key);
                else
                    RemoveSensitiveFields(obj[key]);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                RemoveSensitiveFields(item);
        }
    }
}
