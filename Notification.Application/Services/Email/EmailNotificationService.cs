namespace Notification.Application.Services.Email;

using System.Threading;
using System.Threading.Tasks;
using Notification.Application.Interfaces;

public sealed class EmailNotificationService : IEmailNotificationService
{
    private readonly IEmailSenderService _emailSender;
    private readonly IEmailBackgroundQueue? _backgroundQueue;

    public EmailNotificationService(
        IEmailSenderService emailSender,
        IEmailBackgroundQueue? backgroundQueue = null)
    {
        _emailSender = emailSender;
        _backgroundQueue = backgroundQueue;
    }

    public async Task<bool> SendOtpEmailAsync(SendOtpEmailDto dto, CancellationToken cancellationToken = default)
    {
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
        var subject = "Thông báo từ chối tài khoản Seller";
        var htmlBody = $"<p>Xin chào <strong>{dto.FullName}</strong>,</p><p>Rất tiếc, cửa hàng <strong>{dto.ShopName}</strong> của bạn chưa được phê duyệt.</p><p><strong>Lý do:</strong> {dto.RejectReason}</p>";

        var generalDto = new SendGeneralEmailDto(dto.Email, subject, htmlBody);
        return await SendEmailAsync(generalDto, cancellationToken);
    }
}
