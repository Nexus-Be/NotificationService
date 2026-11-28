namespace Notification.Infrastructure.Services.Email;

public sealed class ResendOptions
{
    public const string SectionName = "Resend";
    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Nexus";
    public string BaseUrl { get; set; } = "https://api.resend.com/";
}
