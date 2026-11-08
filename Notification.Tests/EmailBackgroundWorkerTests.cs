namespace Notification.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Application.Interfaces;
using Notification.Application.Services.Email;
using Notification.Infrastructure.BackgroundWorkers;
using Notification.Infrastructure.Services.Email;
using Xunit;

public sealed class EmailBackgroundWorkerTests
{
    [Theory]
    [InlineData("password_reset")]
    [InlineData("seller_registration")]
    public async Task Worker_UsesQueuedPurposeInsteadOfRegistration(string purpose)
    {
        var queue = new EmailBackgroundQueue();
        var captured = new TaskCompletionSource<SendOtpEmailDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new Mock<IEmailSenderService>();
        sender.Setup(s => s.SendOtpEmailAsync(It.IsAny<SendOtpEmailDto>(), It.IsAny<CancellationToken>()))
            .Callback<SendOtpEmailDto, CancellationToken>((dto, _) => captured.TrySetResult(dto)).ReturnsAsync(true);
        using var worker = new EmailBackgroundWorker(queue, sender.Object, NullLogger<EmailBackgroundWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var service = new EmailNotificationService(sender.Object, queue);
            var response = await service.DispatchOtpAsync(new SendOtpEmailRequest { Email = "user@example.com", OtpCode = "123456", Purpose = purpose });
            Assert.Equal("Queued", response.Status);
            var sent = await captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(purpose, sent.Purpose);
            Assert.Equal("123456", sent.OtpCode);
            sender.Verify(s => s.SendOtpRegistrationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(timeout.Token);
        }
    }
}
