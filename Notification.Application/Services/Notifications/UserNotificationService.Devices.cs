namespace Notification.Application.Services.Notifications;

using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Common.Extensions;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

public sealed partial class UserNotificationService
{
    public async Task<DeviceRegisteredResponse> RegisterDeviceAsync(Guid userId, RegisterDeviceRequest request, CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        if (request is null) throw DeviceInvalid();
        var token = NormalizeDeviceToken(request.Token);
        var previous = request.PreviousToken is null ? null : NormalizeDeviceToken(request.PreviousToken);
        if (previous == token) throw DeviceInvalid();
        var platformName = Enum.GetNames<DevicePlatform>().FirstOrDefault(n => string.Equals(n, request.Platform?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (platformName is null) throw DeviceInvalid();
        var platform = Enum.Parse<DevicePlatform>(platformName);
        var keys = previous is null ? new[] { token } : new[] { token, previous };
        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
        await LockDeviceTokensAsync(keys, cancellationToken);
        var rows = new Dictionary<string, DeviceToken?>(StringComparer.Ordinal);
        foreach (var key in keys.OrderBy(TokenFingerprint, StringComparer.Ordinal))
            rows[key] = await ReadDeviceForUpdateAsync(key, cancellationToken);
        var row = rows[token]; var old = previous is null ? null : rows[previous];
        if (previous is not null && (old is null || old.UserId != userId || old.IsDeleted))
            throw new DomainException("DEVICE_TOKEN_NOT_FOUND", "Previous device token was not found or does not belong to the user.", 404);
        if (row is { IsDeleted: true })
            throw new DomainException("DEVICE_TOKEN_UNAVAILABLE", "Device registration is unavailable.", 409);
        if (row is { IsActive: true } && row.UserId != userId)
            throw new DomainException("DEVICE_TOKEN_OWNERSHIP_CONFLICT", "Active device token belongs to another account; revoke it before switching accounts.", 409);
        var before = row is null ? null : DeviceAuditState(row);
        var operation = row is null ? "Created" : row.UserId != userId ? "Transferred" : row.IsActive ? "Refreshed" : "Reactivated";
        if (old is { IsActive: true }) await DeactivateDeviceAsync(old, userId, "TokenRotated", cancellationToken);
        var stamp = DeviceStamp(row?.UpdatedAt);
        if (row is null)
        {
            row = new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = token,
                Platform = platform,
                IsActive = true,
                LastSeenAt = stamp,
                CreatedAt = stamp,
                UpdatedAt = stamp
            };
            _db.DeviceTokens.Add(row);
        }
        else
        {
            if (_db.Database.IsRelational())
                await _db.DeviceTokens.IgnoreQueryFilters().Where(d => d.Id == row.Id && !d.IsDeleted).ExecuteUpdateAsync(set => set
                    .SetProperty(d => d.UserId, userId).SetProperty(d => d.Platform, platform).SetProperty(d => d.IsActive, true)
                    .SetProperty(d => d.LastSeenAt, stamp).SetProperty(d => d.UpdatedAt, stamp), cancellationToken);
            row.UserId = userId; row.Platform = platform; row.IsActive = true; row.LastSeenAt = stamp; row.UpdatedAt = stamp;
        }
        _db.AddAuditLog(AuditAction.DeviceRegister, nameof(DeviceToken), row.Id.ToString(), oldValues: before,
            newValues: new { Operation = operation, Device = DeviceAuditState(row) }, userId: userId.ToString());
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
        {
            throw new DomainException("DEVICE_TOKEN_CONFLICT", "Device registration changed concurrently; retry the request.", 409);
        }
        return new(platform.ToString(), token, stamp);
    }

    public async Task RevokeDeviceAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId); token = NormalizeDeviceToken(token);
        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
        await LockDeviceTokensAsync(new[] { token }, cancellationToken);
        var row = await ReadDeviceForUpdateAsync(token, cancellationToken);
        if (row is { IsActive: true, IsDeleted: false } && row.UserId == userId)
        {
            await DeactivateDeviceAsync(row, userId, "Logout", cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    private async Task DeactivateDeviceAsync(DeviceToken row, Guid actor, string reason, CancellationToken ct)
    {
        var before = DeviceAuditState(row); var stamp = DeviceStamp(row.UpdatedAt);
        if (_db.Database.IsRelational())
            await _db.DeviceTokens.IgnoreQueryFilters().Where(d => d.Id == row.Id && d.UserId == actor && !d.IsDeleted)
                .ExecuteUpdateAsync(set => set.SetProperty(d => d.IsActive, false).SetProperty(d => d.UpdatedAt, stamp), ct);
        row.IsActive = false; row.UpdatedAt = stamp;
        _db.AddAuditLog(AuditAction.DeviceRevoke, nameof(DeviceToken), row.Id.ToString(), oldValues: before,
            newValues: new { Reason = reason, Device = DeviceAuditState(row) }, userId: actor.ToString());
    }

    private async Task LockDeviceTokensAsync(IEnumerable<string> tokens, CancellationToken ct)
    {
        if (!_db.Database.IsRelational()) return;
        foreach (var hash in tokens.Select(TokenFingerprint).Distinct(StringComparer.Ordinal).OrderBy(h => h, StringComparer.Ordinal))
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({hash}, 8008))", ct);
    }
    private async Task<DeviceToken?> ReadDeviceForUpdateAsync(string token, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
            return await _db.DeviceTokens.IgnoreQueryFilters().AsTracking().SingleOrDefaultAsync(d => d.Token == token, ct);
        var rows = await _db.DeviceTokens.FromSqlInterpolated($"SELECT * FROM \"DeviceTokens\" WHERE \"Token\"={token} FOR UPDATE")
            .IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        return rows.SingleOrDefault();
    }
    private DateTimeOffset DeviceStamp(DateTimeOffset? previous)
    {
        var now = _time.GetUtcNow().ToUniversalTime(); now = new DateTimeOffset(now.Ticks / 10 * 10, TimeSpan.Zero);
        return previous.HasValue && previous.Value >= now ? previous.Value.ToUniversalTime().AddTicks(10) : now;
    }
    private static string NormalizeDeviceToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw DeviceInvalid();
        var token = value.Trim();
        if (token.Length > 1000 || token.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) throw DeviceInvalid();
        return token;
    }
    private static DomainException DeviceInvalid() => new("VAL_INVALID_INPUT", "Device token, platform or rotation request is invalid.");
    private static string TokenFingerprint(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static object DeviceAuditState(DeviceToken device) => new
    {
        device.Id,
        device.UserId,
        Platform = device.Platform.ToString(),
        device.IsActive,
        device.LastSeenAt,
        device.UpdatedAt,
        TokenFingerprint = TokenFingerprint(device.Token)
    };
}
