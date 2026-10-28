namespace Notification.Application.Consumers.User;

using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Logging;
using Nexus.Contracts.Events;
using Notification.Application.Common.Interfaces;

public sealed class SendOtpEmailConsumer : IConsumer<SendOtpEmailIntegrationEvent>
{
    private readonly IEmailSenderService _emailSender;
    private readonly ILogger<SendOtpEmailConsumer> _logger;

    public SendOtpEmailConsumer(
        IEmailSenderService emailSender,
        ILogger<SendOtpEmailConsumer> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendOtpEmailIntegrationEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation(
            "Received SendOtpEmailIntegrationEvent for recipient '{Email}' (Name: {FullName}). Dispatching email worker...",
            message.Email,
            message.FullName);

        var success = await _emailSender.SendOtpRegistrationEmailAsync(
            toEmail: message.Email,
            fullName: message.FullName,
            otp: message.Otp,
            expiresInMinutes: message.ExpiresInMinutes,
            cancellationToken: context.CancellationToken);

        if (success)
        {
            _logger.LogInformation("Background email worker successfully dispatched OTP email to '{Email}'.", message.Email);
        }
        else
        {
            _logger.LogWarning(
                "Failed to send OTP email to '{Email}'.",
                message.Email
            );
            throw new System.InvalidOperationException(
                "OTP email could not be sent."
            );
        }
    }
}
