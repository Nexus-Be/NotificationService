namespace Notification.Application;

using Microsoft.Extensions.DependencyInjection;
using Notification.Application.Services.Email;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IEmailNotificationService, EmailNotificationService>();
        return services;
    }
}
