namespace Notification.Infrastructure.Email;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = false;
    public string SenderName { get; set; } = "Nexus Ecosystem";
    public string SenderEmail { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
