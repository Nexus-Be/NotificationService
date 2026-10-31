namespace Notification.Infrastructure.BackgroundWorkers;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notification.Application.Interfaces;

public sealed class EmailBackgroundWorker : BackgroundService
{
    private readonly IEmailBackgroundQueue _queue;
    private readonly IEmailSenderService _emailSender;
    private readonly ILogger<EmailBackgroundWorker> _logger;

    public EmailBackgroundWorker(
        IEmailBackgroundQueue queue,
        IEmailSenderService emailSender,
        ILogger<EmailBackgroundWorker> logger)
    {
        _queue = queue;
        _emailSender = emailSender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmailBackgroundWorker is started and listening for email dispatch requests.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var item = await _queue.DequeueOtpEmailAsync(stoppingToken);

                _logger.LogInformation(
                    "[Background Email] Processing OTP email dispatch for '{Email}' ({FullName}).",
                    item.Email,
                    item.FullName);

                var success = await _emailSender.SendOtpRegistrationEmailAsync(
                    item.Email,
                    item.FullName,
                    item.OtpCode,
                    item.ExpiresInMinutes,
                    stoppingToken);

                if (!success)
                {
                    _logger.LogWarning("[Background Email] Failed to deliver OTP email to '{Email}'.", item.Email);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Background Email] Unexpected error occurred while processing OTP email queue.");
            }
        }

        _logger.LogInformation("EmailBackgroundWorker is shutting down.");
    }
}
