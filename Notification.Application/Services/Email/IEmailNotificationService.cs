namespace Notification.Application.Services.Email;

using System.Threading;
using System.Threading.Tasks;

public interface IEmailNotificationService
{
    Task<bool> SendOtpEmailAsync(SendOtpEmailDto dto, CancellationToken cancellationToken = default);
    Task<bool> SendEmailAsync(SendGeneralEmailDto dto, CancellationToken cancellationToken = default);
    Task<bool> SendSellerApprovalEmailAsync(SendSellerApprovalEmailDto dto, CancellationToken cancellationToken = default);
    Task<bool> SendSellerRejectionEmailAsync(SendSellerRejectionEmailDto dto, CancellationToken cancellationToken = default);
}

// Khai báo các DTO record phục vụ cho seller
public sealed record SendSellerApprovalEmailDto(
    string Email,
    string FullName,
    string ShopName);

public sealed record SendSellerRejectionEmailDto(
    string Email,
    string FullName,
    string ShopName,
    string RejectReason);