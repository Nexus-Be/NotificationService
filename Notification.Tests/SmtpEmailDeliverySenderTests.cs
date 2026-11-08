namespace Notification.Tests;

using System.Net;
using System.Net.Sockets;
using System.Text;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Notification.Application.Services.Deliveries;
using Notification.Infrastructure.Services.Email;
using Xunit;

public sealed class SmtpEmailDeliverySenderTests
{
    private static SmtpEmailDeliverySender Sender(SmtpOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<SmtpOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(options);
        return new(monitor.Object, NullLogger<SmtpEmailDeliverySender>.Instance);
    }

    [Fact]
    public async Task MissingCredentials_DoNotSimulateSuccess()
    {
        var result = await Sender(new SmtpOptions()).SendAsync(new(Guid.NewGuid(), "user@example.test", "Hello", "Body"));
        Assert.False(result.Succeeded);
        Assert.False(result.IsTransient);
        Assert.Equal("SMTP_NOT_CONFIGURED", result.ErrorCode);
    }

    [Theory]
    [InlineData(450, true)]
    [InlineData(451, true)]
    [InlineData(550, false)]
    [InlineData(535, false)]
    public void SmtpResponses_ClassifyWithoutLeakingRawError(int status, bool transient)
    {
        var result = SmtpEmailDeliverySender.ClassifyFailure(new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, (SmtpStatusCode)status, "raw-provider-sensitive-message"));
        Assert.Equal(transient, result.IsTransient);
        Assert.Equal("SMTP_" + status, result.ErrorCode);
        Assert.DoesNotContain("raw-provider", result.ErrorMessage!);
    }

    [Fact]
    public void NetworkAndAuthenticationFailures_AreDifferent()
    {
        Assert.True(SmtpEmailDeliverySender.ClassifyFailure(new IOException("network")).IsTransient);
        Assert.False(SmtpEmailDeliverySender.ClassifyFailure(new MailKit.Security.AuthenticationException("secret")).IsTransient);
        Assert.False(SmtpEmailDeliverySender.ClassifyFailure(new FormatException("address")).IsTransient);
    }

    [Fact]
    public async Task LocalSmtp_AcceptsMessageWithStableId_EvenWhenQuitDisconnects()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new StringBuilder();
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };
            await writer.WriteLineAsync("220 local-test ESMTP");
            while (!timeout.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(timeout.Token);
                if (line is null) break;
                if (line.StartsWith("EHLO"))
                {
                    await writer.WriteLineAsync("250-local-test");
                    await writer.WriteLineAsync("250 AUTH PLAIN");
                }
                else if (line.StartsWith("AUTH")) await writer.WriteLineAsync("235 authenticated");
                else if (line.StartsWith("MAIL FROM") || line.StartsWith("RCPT TO")) await writer.WriteLineAsync("250 accepted");
                else if (line == "DATA")
                {
                    await writer.WriteLineAsync("354 send data");
                    while ((line = await reader.ReadLineAsync(timeout.Token)) is not null && line != ".") received.AppendLine(line);
                    await writer.WriteLineAsync("250 queued");
                }
                else if (line == "QUIT") break; // Deliberate EOF after acceptance.
                else await writer.WriteLineAsync("250 ok");
            }
        }, timeout.Token);
        var message = new EmailDeliveryMessage(Guid.NewGuid(), "user@example.test", "Subject", "<p>Body</p>");
        var sender = Sender(new SmtpOptions { Host = "127.0.0.1", Port = port, Username = "sender@example.test", Password = "local-test-only", SenderEmail = "sender@example.test" });
        var result = await sender.SendAsync(message, timeout.Token);
        await server.WaitAsync(timeout.Token);
        Assert.True(result.Succeeded);
        Assert.Contains("Message-Id: <" + message.MessageId + ">", received.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(message.MessageId, (message with { Subject = "Retry" }).MessageId);
    }

    [Theory]
    [InlineData(0, 30, 120)]
    [InlineData(11, 30, 120)]
    [InlineData(3, 30, 40)]
    public void WorkerSettings_RejectUnsafeLimits(int attempts, int timeout, int lease)
    {
        Assert.Throws<InvalidOperationException>(() => new DeliveryWorkerSettings { MaxAttempts = attempts, AttemptTimeoutSeconds = timeout, LeaseSeconds = lease }.Validate());
    }
}
