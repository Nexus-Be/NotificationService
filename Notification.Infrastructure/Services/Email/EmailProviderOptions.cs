namespace Notification.Infrastructure.Services.Email;

public sealed class EmailProviderOptions
{
    public const string SectionName = "Email";

    public string Provider { get; set; } = "Smtp";
}
