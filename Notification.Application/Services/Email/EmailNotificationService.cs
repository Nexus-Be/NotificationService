namespace Notification.Application.Services.Email;

using System.Net;
using Notification.Domain.Exceptions;
using System.Threading;
using System.Threading.Tasks;
using Notification.Application.Interfaces;

public sealed class EmailNotificationService : IEmailNotificationService
{
    private readonly IEmailSenderService _emailSender;
    private readonly IEmailBackgroundQueue? _backgroundQueue;
    private readonly IOtpEmailDispatchService? _otpDispatch;

    public EmailNotificationService(
        IEmailSenderService emailSender,
        IEmailBackgroundQueue? backgroundQueue = null,
        IOtpEmailDispatchService? otpDispatch = null)
    {
        _emailSender = emailSender;
        _backgroundQueue = backgroundQueue;
        _otpDispatch = otpDispatch;
    }

    public async Task<OtpEmailDispatchResponse> DispatchOtpAsync(SendOtpEmailRequest request, CancellationToken cancellationToken = default)
    {
        if (_otpDispatch is not null) return await _otpDispatch.EnqueueAsync(request, cancellationToken);
        var fullName = string.IsNullOrWhiteSpace(request.FullName) ? "Customer" : request.FullName.Trim();
        var purpose = string.IsNullOrWhiteSpace(request.Purpose) ? "registration" : request.Purpose.Trim().ToLowerInvariant();
        var dto = new SendOtpEmailDto(request.Email.Trim(), fullName, request.OtpCode.Trim(), request.ExpiresInMinutes, purpose);
        EnsureSent(await SendOtpEmailAsync(dto, cancellationToken));
        return new(dto.Email, fullName, dto.ExpiresInMinutes, purpose,
            _backgroundQueue is null ? "Sent" : "Queued", DateTimeOffset.UtcNow);
    }

    public async Task<EmailDispatchResponse> DispatchEmailAsync(SendGeneralEmailRequest request, CancellationToken cancellationToken = default)
    {
        var dto = new SendGeneralEmailDto(request.ToEmail.Trim(), request.Subject.Trim(), request.HtmlBody);
        EnsureSent(await SendEmailAsync(dto, cancellationToken));
        return new(dto.ToEmail, "Sent", DateTimeOffset.UtcNow);
    }

    public async Task<EmailDispatchResponse> DispatchSellerRejectionAsync(SendSellerRejectionEmailRequest request, CancellationToken cancellationToken = default)
    {
        var dto = new SendSellerRejectionEmailDto(request.Email.Trim(),
            string.IsNullOrWhiteSpace(request.FullName) ? "Customer" : request.FullName.Trim(),
            string.IsNullOrWhiteSpace(request.ShopName) ? "Your Shop" : request.ShopName.Trim(), request.RejectReason.Trim());
        EnsureSent(await SendSellerRejectionEmailAsync(dto, cancellationToken));
        return new(dto.Email, "Sent", DateTimeOffset.UtcNow);
    }

    private static void EnsureSent(bool sent)
    {
        if (!sent)
            throw new DomainException("EMAIL_SEND_FAILED", "Failed to deliver email via notification provider.", 500);
    }

    public async Task<bool> SendOtpEmailAsync(SendOtpEmailDto dto, CancellationToken cancellationToken = default)
    {
        if (_otpDispatch is not null)
        {
            await _otpDispatch.EnqueueAsync(new SendOtpEmailRequest { Email = dto.Email, FullName = dto.FullName, OtpCode = dto.OtpCode, Purpose = dto.Purpose, ExpiresInMinutes = dto.ExpiresInMinutes }, cancellationToken);
            return true;
        }
        if (_backgroundQueue != null)
        {
            await _backgroundQueue.QueueOtpEmailAsync(dto, cancellationToken);
            return true;
        }

        return await _emailSender.SendOtpEmailAsync(dto, cancellationToken);
    }

    public Task<bool> SendEmailAsync(SendGeneralEmailDto dto, CancellationToken cancellationToken = default)
    {
        return _emailSender.SendEmailAsync(dto.ToEmail, dto.Subject, dto.HtmlBody, cancellationToken);
    }

    public async Task<bool> SendSellerApprovalEmailAsync(SendSellerApprovalEmailDto dto, CancellationToken cancellationToken = default)
    {
        var subject = "Thông báo phê duyệt tài khoản Seller";
        var htmlBody = $"<p>Xin chào <strong>{dto.FullName}</strong>,</p><p>Cửa hàng <strong>{dto.ShopName}</strong> của bạn đã được phê duyệt thành công trên hệ thống Nexus!</p>";

        var generalDto = new SendGeneralEmailDto(dto.Email, subject, htmlBody);
        return await SendEmailAsync(generalDto, cancellationToken);
    }

    public async Task<bool> SendSellerRejectionEmailAsync(SendSellerRejectionEmailDto dto, CancellationToken cancellationToken = default)
    {
        var fullName = WebUtility.HtmlEncode(dto.FullName);
        var shopName = WebUtility.HtmlEncode(dto.ShopName);
        var reason = WebUtility.HtmlEncode(dto.RejectReason);
        var subject = "Thông báo từ chối tài khoản Seller";
        var htmlBody = $"<p>Xin chào <strong>{fullName}</strong>,</p><p>Rất tiếc, cửa hàng <strong>{shopName}</strong> của bạn chưa được phê duyệt.</p><p><strong>Lý do:</strong> {reason}</p>";

        var generalDto = new SendGeneralEmailDto(dto.Email, subject, htmlBody);
        return await SendEmailAsync(generalDto, cancellationToken);
    }
}
