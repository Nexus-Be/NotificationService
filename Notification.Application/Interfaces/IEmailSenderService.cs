namespace Notification.Application.Interfaces;

using System.Threading;
using System.Threading.Tasks;

public interface IEmailSenderService
{
    Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
    Task<bool> SendOtpRegistrationEmailAsync(string toEmail, string fullName, string otp, int expiresInMinutes = 5, CancellationToken cancellationToken = default);
}
