namespace Notification.Infrastructure.BackgroundWorkers;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notification.Application.Interfaces;
using Notification.Application.Services.Health;

public sealed class EmailBackgroundWorker : BackgroundService
{
    private readonly IEmailBackgroundQueue _queue;
    private readonly IEmailSenderService _emailSender;
    private readonly ILogger<EmailBackgroundWorker> _logger;
    private readonly IWorkerHealthReporter? _health;
    public EmailBackgroundWorker(IEmailBackgroundQueue queue, IEmailSenderService emailSender,
        ILogger<EmailBackgroundWorker> logger, IWorkerHealthReporter? health = null)
    {
        _queue = queue; _emailSender = emailSender; _logger = logger; _health = health;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmailBackgroundWorker is started and listening for email dispatch requests.");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _health?.Report(WorkerName.OtpEmail,WorkerActivity.Waiting);
                try
                {
                    var item = await _queue.DequeueOtpEmailAsync(stoppingToken);
                    _health?.Report(WorkerName.OtpEmail,WorkerActivity.Running);
                    _logger.LogInformation("[Background Email] Processing OTP email dispatch for '{Email}' ({FullName}).",item.Email,item.FullName);
                    var success = await _emailSender.SendOtpEmailAsync(item,stoppingToken);
                    if (!success) _logger.LogWarning("[Background Email] Failed to deliver OTP email to '{Email}'.",item.Email);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _health?.Report(WorkerName.OtpEmail,WorkerActivity.Failed);
                    _logger.LogError(ex,"[Background Email] Unexpected error occurred while processing OTP email queue.");
                    try { await Task.Delay(TimeSpan.FromSeconds(1),stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                }
            }
        }
        finally
        {
            _health?.Report(WorkerName.OtpEmail,WorkerActivity.Stopped);
            _logger.LogInformation("EmailBackgroundWorker is shutting down.");
        }
    }
}
