namespace Notification.Application.Services.Email;

using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Common.Extensions;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

public sealed partial class OtpEmailDispatchService(
    INotificationDbContext db,IOtpPayloadProtector protector,IEmailDeliverySender sender,OtpWorkerSettings settings,TimeProvider time) : IOtpEmailDispatchService
{
    private sealed record Secret(string Code,string FullName);
    private static DomainException Invalid() => new("VAL_INVALID_INPUT","OTP request fields or expiry are invalid.");
    private static DateTimeOffset Round(DateTimeOffset value) => new(value.ToUniversalTime().Ticks / 10 * 10,TimeSpan.Zero);
    private DateTimeOffset Now => Round(time.GetUtcNow());
    private async Task GroupLock(string recipient,string purpose,CancellationToken ct)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recipient + "\n" + purpose)));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8009))",ct);
    }
    private Task<OtpEmailDispatch?> Find(Guid id,CancellationToken ct) =>
        db.OtpEmailDispatches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(j => j.Id == id,ct);
    private static bool SecretText(string? value,int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));

    public async Task<OtpEmailDispatchResponse> EnqueueAsync(SendOtpEmailRequest request,CancellationToken cancellationToken = default)
    {
        settings.Validate();
        if (request is null || !SecretText(request.Email?.Trim(),254) || !new EmailAddressAttribute().IsValid(request.Email?.Trim()) ||
            !SecretText(request.OtpCode?.Trim(),32) || request.ExpiresInMinutes is < 1 or > 60 || request.RequestId == Guid.Empty || request.UserId == Guid.Empty ||
            request.IssuedAt.HasValue != request.ExpiresAt.HasValue || request.FullName?.Length > 200 || request.TraceId?.Length > 100 ||
            request.FullName?.Any(char.IsControl) == true || request.TraceId?.Any(char.IsControl) == true) throw Invalid();
        var recipient = request.Email!.Trim().ToLowerInvariant();
        var name = string.IsNullOrWhiteSpace(request.FullName) ? "Customer" : request.FullName.Trim();
        var purpose = string.IsNullOrWhiteSpace(request.Purpose) ? "registration" : request.Purpose.Trim().ToLowerInvariant();
        if (purpose is not ("registration" or "password_reset" or "seller_registration")) throw Invalid();
        var id = request.RequestId ?? Guid.NewGuid(); var existing = await Find(id,cancellationToken); var now = Now;
        var issued = request.IssuedAt.HasValue ? Round(request.IssuedAt.Value) : existing?.IssuedAt ?? now;
        var expires = request.ExpiresAt.HasValue ? Round(request.ExpiresAt.Value) : existing?.ExpiresAt ?? issued.AddMinutes(request.ExpiresInMinutes);
        if (issued > now.AddSeconds(30) || expires <= issued || expires > issued.AddMinutes(60)) throw Invalid();
        var code = request.OtpCode!.Trim();
        var proof = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            recipient,name,code,purpose,request.UserId,IssuedAt = request.IssuedAt.HasValue ? issued : (DateTimeOffset?)null,
            ExpiresAt = request.ExpiresAt.HasValue ? expires : (DateTimeOffset?)null,request.ExpiresInMinutes
        }))));
        if (existing is not null) return Replay(existing,proof);
        if (expires <= now) throw new DomainException("OTP_EXPIRED","OTP request has already expired.");
        var row = new OtpEmailDispatch
        {
            Id = id,Recipient = recipient,RecipientKey = recipient,Purpose = purpose,FullName = name,UserId = request.UserId,
            IssuedAt = issued,ExpiresAt = expires,RelativeMinutes = request.ExpiresInMinutes,Status = OtpDispatchStatus.Queued,
            ProtectedPayload = protector.ProtectPayload(JsonSerializer.Serialize(new Secret(code,name))),
            ProtectedFingerprint = protector.ProtectFingerprint(proof),TraceId = request.TraceId,
            NextRetryAt = now,CreatedAt = now,UpdatedAt = now
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await GroupLock(recipient,purpose,cancellationToken);
        existing = await Find(id,cancellationToken);
        if (existing is not null) return Replay(existing,proof);
        var latest = await db.OtpEmailDispatches.IgnoreQueryFilters().AsNoTracking().Where(j => j.RecipientKey == recipient && j.Purpose == purpose)
            .OrderByDescending(j => j.IssuedAt).FirstOrDefaultAsync(cancellationToken);
        if (latest is not null && latest.IssuedAt >= issued)
            throw new DomainException("OTP_REQUEST_OBSOLETE","OTP request is older than or equal to the latest challenge.",409);
        var audit = new List<AuditLog>();
        var older = await db.OtpEmailDispatches.IgnoreQueryFilters().AsNoTracking().Where(j => j.RecipientKey == recipient && j.Purpose == purpose &&
            (j.Status == OtpDispatchStatus.Queued || j.Status == OtpDispatchStatus.Sending)).ToListAsync(cancellationToken);
        foreach (var old in older)
        {
            await db.OtpEmailDispatches.IgnoreQueryFilters().Where(j => j.Id == old.Id).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status,OtpDispatchStatus.Superseded).SetProperty(j => j.ProtectedPayload,(string?)null)
                .SetProperty(j => j.NextRetryAt,(DateTimeOffset?)null).SetProperty(j => j.RowVersion,j => j.RowVersion + 1)
                .SetProperty(j => j.ErrorCode,"OTP_SUPERSEDED").SetProperty(j => j.UpdatedAt,now),cancellationToken);
            audit.Add(Audit(old,AuditAction.OtpResult,"Superseded","OTP_SUPERSEDED"));
        }
        db.OtpEmailDispatches.Add(row); audit.Add(Audit(row,AuditAction.OtpAccepted,"Queued",null));
        try { await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
        {
            await transaction.RollbackAsync(cancellationToken); await transaction.DisposeAsync();
            db.OtpEmailDispatches.Remove(row); db.AuditLogs.RemoveRange(audit);
            existing = await Find(id,cancellationToken); if (existing is null) throw;
            return Replay(existing,proof);
        }
        return Response(row,false);
    }
    private OtpEmailDispatchResponse Replay(OtpEmailDispatch old,string fingerprint)
    {
        string original;
        try { original = protector.UnprotectFingerprint(old.ProtectedFingerprint); }
        catch (CryptographicException) { throw new DomainException("OTP_PROTECTION_UNAVAILABLE","OTP request protection is unavailable.",503); }
        if (original != fingerprint) throw new DomainException("OTP_REQUEST_CONFLICT","OTP request ID was already used for different fields.",409);
        return Response(old,true);
    }
    private static OtpEmailDispatchResponse Response(OtpEmailDispatch row,bool replay) =>
        new(row.Recipient,row.FullName,(int)Math.Ceiling((row.ExpiresAt-row.IssuedAt).TotalMinutes),row.Purpose,row.Status.ToString(),row.CreatedAt,row.Id,row.ExpiresAt,replay);
    public async Task<OtpDispatchStatusResponse> GetAsync(Guid id,CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) throw Invalid();
        var row = await db.OtpEmailDispatches.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id,cancellationToken);
        if (row is null) throw new DomainException("OTP_DISPATCH_NOT_FOUND","OTP dispatch was not found.",404);
        var at = row.Recipient.LastIndexOf('@'); var masked = at > 0 ? "***" + row.Recipient[at..] : "Recipient hidden";
        return new(row.Id,masked,row.Purpose,row.Status.ToString(),row.AttemptCount,row.IssuedAt,row.ExpiresAt,row.NextRetryAt,row.ErrorCode,row.CreatedAt,row.UpdatedAt);
    }
    private AuditLog Audit(OtpEmailDispatch row,AuditAction action,string status,string? error) =>
        db.AddAuditLog(action,nameof(OtpEmailDispatch),row.Id.ToString(),newValues:new
        {
            RequestId = row.Id,row.Recipient,row.Purpose,Status = status,row.AttemptCount,row.IssuedAt,row.ExpiresAt,ErrorCode = error,Timestamp = Now
        },userId:row.UserId?.ToString(),traceId:row.TraceId);
}
