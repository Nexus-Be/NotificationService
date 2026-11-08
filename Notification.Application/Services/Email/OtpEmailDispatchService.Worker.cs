namespace Notification.Application.Services.Email;

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Services.Deliveries;
using Notification.Domain.Entities;
using Notification.Domain.Enums;

public sealed partial class OtpEmailDispatchService
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        settings.Validate(); var now = Now; var lease = now.AddSeconds(-settings.LeaseSeconds);
        var candidate = await db.OtpEmailDispatches.AsNoTracking().Where(j =>
            (j.Status == OtpDispatchStatus.Queued && (!j.NextRetryAt.HasValue || j.NextRetryAt <= now)) ||
            (j.Status == OtpDispatchStatus.Sending && (!j.LastAttemptAt.HasValue || j.LastAttemptAt <= lease)))
            .OrderBy(j => j.NextRetryAt ?? j.CreatedAt).ThenBy(j => j.Id).FirstOrDefaultAsync(cancellationToken);
        if (candidate is null) return false;
        OtpEmailDispatch claim;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await GroupLock(candidate.RecipientKey,candidate.Purpose,cancellationToken);
            var row = await Find(candidate.Id,cancellationToken); now = Now;
            if (row is null || row.IsDeleted || row.RowVersion != candidate.RowVersion ||
                !((row.Status == OtpDispatchStatus.Queued && (!row.NextRetryAt.HasValue || row.NextRetryAt <= now)) ||
                  (row.Status == OtpDispatchStatus.Sending && (!row.LastAttemptAt.HasValue || row.LastAttemptAt <= now.AddSeconds(-settings.LeaseSeconds)))))
                return true;
            if (row.ExpiresAt <= now || row.AttemptCount >= settings.MaxAttempts)
            {
                var status = row.ExpiresAt <= now ? OtpDispatchStatus.Expired : OtpDispatchStatus.Failed;
                var error = status == OtpDispatchStatus.Expired ? "OTP_EXPIRED" : "OTP_RETRY_LIMIT";
                await Update(row,status,error,null,now,true,cancellationToken);
                Audit(row,AuditAction.OtpResult,status.ToString(),error);
                await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return true;
            }
            var version = row.RowVersion + 1; var attempt = row.AttemptCount + 1;
            await db.OtpEmailDispatches.Where(j => j.Id == row.Id && j.RowVersion == row.RowVersion).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status,OtpDispatchStatus.Sending).SetProperty(j => j.AttemptCount,attempt)
                .SetProperty(j => j.LastAttemptAt,now).SetProperty(j => j.NextRetryAt,(DateTimeOffset?)null)
                .SetProperty(j => j.RowVersion,version).SetProperty(j => j.UpdatedAt,now),cancellationToken);
            row.Status = OtpDispatchStatus.Sending; row.AttemptCount = attempt; row.RowVersion = version;
            Audit(row,AuditAction.OtpAttempt,"Sending",null);
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); claim = row;
        }
        EmailDeliveryResult result;
        try
        {
            var payload = claim.ProtectedPayload is null ? null : JsonSerializer.Deserialize<Secret>(protector.UnprotectPayload(claim.ProtectedPayload));
            if (payload is null) throw new CryptographicException();
            var remaining = claim.ExpiresAt - Now;
            if (remaining <= TimeSpan.Zero) result = EmailDeliveryResult.Failure("OTP_EXPIRED","OTP request expired.");
            else
            {
                var budget = TimeSpan.FromSeconds(settings.SendTimeoutSeconds);
                if (remaining < budget) budget = remaining;
                using var timeout = new CancellationTokenSource(budget,time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,timeout.Token);
                var title = claim.Purpose switch
                {
                    "password_reset" => "Nexus - Password reset verification code",
                    "seller_registration" => "Nexus - Seller registration verification code",
                    _ => "Nexus - Registration verification code"
                };
                var body = $"<p>Hello {WebUtility.HtmlEncode(payload.FullName)},</p><p>Your verification code: <strong>{WebUtility.HtmlEncode(payload.Code)}</strong>.</p>" +
                    $"<p>Purpose: {WebUtility.HtmlEncode(claim.Purpose)}. Expires at {claim.ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC.</p>";
                result = await sender.SendAsync(new(claim.Id,claim.Recipient,title,body),linked.Token);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { result = EmailDeliveryResult.Failure("OTP_SEND_TIMEOUT","OTP provider timed out.",true); }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        { result = EmailDeliveryResult.Failure("OTP_PAYLOAD_UNAVAILABLE","OTP payload protection is unavailable."); }
        catch (Exception) { result = EmailDeliveryResult.Failure("OTP_SEND_FAILED","OTP provider request failed.",true); }
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await GroupLock(claim.RecipientKey,claim.Purpose,cancellationToken);
            var current = await Find(claim.Id,cancellationToken); now = Now;
            if (current is null || current.IsDeleted || current.Status != OtpDispatchStatus.Sending || current.RowVersion != claim.RowVersion) return true;
            var due = now.AddSeconds(settings.BaseDelaySeconds * Math.Pow(2,current.AttemptCount - 1));
            var retry = !result.Succeeded && result.IsTransient && current.AttemptCount < settings.MaxAttempts && due < current.ExpiresAt;
            var expired = !result.Succeeded && (current.ExpiresAt <= now || result.ErrorCode == "OTP_EXPIRED" || (result.IsTransient && due >= current.ExpiresAt));
            var status = result.Succeeded ? OtpDispatchStatus.ProviderAccepted : expired ? OtpDispatchStatus.Expired : retry ? OtpDispatchStatus.Queued : OtpDispatchStatus.Failed;
            var code = result.Succeeded ? null : expired ? "OTP_EXPIRED" : SafeCode(result.ErrorCode);
            await Update(current,status,code,retry ? due : null,now,!retry,cancellationToken);
            Audit(current,AuditAction.OtpResult,status.ToString(),code);
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        }
        return true;
    }
    private Task<int> Update(OtpEmailDispatch row,OtpDispatchStatus status,string? error,DateTimeOffset? retry,
        DateTimeOffset now,bool clear,CancellationToken ct) =>
        db.OtpEmailDispatches.IgnoreQueryFilters().Where(j => j.Id == row.Id && j.RowVersion == row.RowVersion).ExecuteUpdateAsync(s => s
            .SetProperty(j => j.Status,status).SetProperty(j => j.ErrorCode,error).SetProperty(j => j.NextRetryAt,retry)
            .SetProperty(j => j.ProtectedPayload,clear ? null : row.ProtectedPayload)
            .SetProperty(j => j.RowVersion,row.RowVersion + 1).SetProperty(j => j.UpdatedAt,now),ct);
    private static string SafeCode(string? value) => value is { Length: > 0 and <= 100 } && Regex.IsMatch(value,@"^[A-Z][A-Z0-9_]*$")
        ? value : "OTP_SEND_FAILED";
}
