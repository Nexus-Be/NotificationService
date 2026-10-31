namespace Notification.Tests;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Notification.Infrastructure.Services.Email;
using Xunit;

public sealed class EmailSenderTests
{
    [Fact]
    public async Task SendOtpRegistrationEmailAsync_simulation_mode_when_credentials_empty()
    {
        var optionsMonitor = new FakeOptionsMonitor<SmtpOptions>(new SmtpOptions
        {
            Host = "smtp.gmail.com",
            Port = 587,
            Username = "",
            Password = ""
        });

        var emailSender = new MailKitEmailSenderService(optionsMonitor, NullLogger<MailKitEmailSenderService>.Instance);

        var result = await emailSender.SendOtpRegistrationEmailAsync(
            toEmail: "user@example.com",
            fullName: "Test User",
            otp: "123456",
            expiresInMinutes: 5);

        Assert.True(result);
    }

    private sealed class FakeOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public FakeOptionsMonitor(T currentValue) => CurrentValue = currentValue;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
