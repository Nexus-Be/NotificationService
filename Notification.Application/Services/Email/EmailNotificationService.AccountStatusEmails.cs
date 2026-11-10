namespace Notification.Application.Services.Email;

using System.ComponentModel.DataAnnotations;
using System.Net;
using Notification.Application.Services.Deliveries;
using Notification.Domain.Exceptions;

public sealed partial class EmailNotificationService
{
    public Task<EmailDispatchResponse> DispatchUserBannedAsync(
        SendUserBannedEmailRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw InvalidAccountEmail();
        return DispatchAccountEmailAsync(request.Email, request.FullName, request.Reason, true, cancellationToken);
    }

    public Task<EmailDispatchResponse> DispatchUserUnbannedAsync(
        SendUserUnbannedEmailRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw InvalidAccountEmail();
        return DispatchAccountEmailAsync(request.Email, request.FullName, request.Reason, false, cancellationToken);
    }

    private async Task<EmailDispatchResponse> DispatchAccountEmailAsync(
        string email, string? fullName, string? reason, bool banned, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || email.Any(char.IsControl) ||
            !new EmailAddressAttribute().IsValid(email.Trim()) || fullName?.Length > 200 ||
            fullName?.Any(char.IsControl) == true || reason?.Length > 1000 ||
            reason?.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) == true ||
            (banned && string.IsNullOrWhiteSpace(reason)))
            throw InvalidAccountEmail();

        if (_strictEmailSender is null)
            throw new DomainException("EMAIL_PROVIDER_UNAVAILABLE", "Email provider is not configured.", 503);

        var recipient = email.Trim();
        var name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(fullName) ? "Khách hàng" : fullName.Trim());
        var subject = banned ? "Nexus - Thông báo khóa tài khoản" : "Nexus - Thông báo mở khóa tài khoản";
        var decision = banned
            ? "Tài khoản của bạn đã bị khóa do vi phạm quy định của hệ thống."
            : "Tài khoản của bạn đã được mở khóa.";
        var reasonHtml = string.IsNullOrWhiteSpace(reason) ? string.Empty
            : $"<p><strong>Lý do:</strong> {WebUtility.HtmlEncode(reason.Trim())}</p>";
        var html = $"<p>Xin chào <strong>{name}</strong>,</p><p>{decision}</p>{reasonHtml}";
        var result = await _strictEmailSender.SendAsync(
            new EmailDeliveryMessage(Guid.NewGuid(), recipient, subject, html), cancellationToken);
        if (!result.Succeeded)
        {
            if (result.ErrorCode == "SMTP_NOT_CONFIGURED")
                throw new DomainException("EMAIL_PROVIDER_UNAVAILABLE", "Email provider is not configured.", 503);
            throw new DomainException("EMAIL_SEND_FAILED", "Failed to deliver email via notification provider.", 500);
        }
        return new EmailDispatchResponse(recipient, "Sent", DateTimeOffset.UtcNow);
    }

    private static DomainException InvalidAccountEmail()
        => new("VAL_INVALID_INPUT", "Account status email fields are invalid.");
}
