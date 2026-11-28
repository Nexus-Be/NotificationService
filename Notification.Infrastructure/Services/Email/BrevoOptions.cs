namespace Notification.Infrastructure.Services.Email;

public sealed class BrevoOptions
{
    public const string SectionName = "Brevo";

    public string BaseUrl { get; set; } = "https://api.brevo.com/v3/";
    public string ApiKey { get; set; } = "";
    public string SenderName { get; set; } = "Nexus";
    public string SenderEmail { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}
