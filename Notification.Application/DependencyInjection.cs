namespace Notification.Application;

using Microsoft.Extensions.DependencyInjection;
using Notification.Application.Services.Email;
using Notification.Application.Services.Notifications;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IEmailNotificationService, EmailNotificationService>();
        services.AddScoped<IUserNotificationService, UserNotificationService>();
        return services;
    }
}
