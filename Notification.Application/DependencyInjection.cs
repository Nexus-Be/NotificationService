
namespace Notification.Application;

using Microsoft.Extensions.DependencyInjection;
using Notification.Application.Services.Email;
using Notification.Application.Services.Notifications;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services)
    {
        // ========================================
        // 1. Email Notification
        // ========================================
        services.AddScoped<
            IEmailNotificationService,
            EmailNotificationService>();

        // ========================================
        // 2. User Notifications - NOTI-05, NOTI-06
        // ========================================
        services.AddScoped<
            IUserNotificationService,
            UserNotificationService>();

        // ========================================
        // 3. Device Tokens - NOTI-01, NOTI-02
        // ========================================
        services.AddScoped<
            IDeviceTokenService,
            DeviceTokenService>();

        // ========================================
        // 4. Notification Preferences
        // NOTI-03, NOTI-04
        // ========================================
        services.AddScoped<
            INotificationPreferenceService,
            NotificationPreferenceService>();

        // ========================================
        // 5. Admin Notification Channels - NOTI-07
        // ========================================
        services.AddScoped<
            INotificationChannelService,
            NotificationChannelService>();

        // ========================================
        // 6. Admin Notification Templates - NOTI-08
        // ========================================
        services.AddScoped<
            INotificationTemplateService,
            NotificationTemplateService>();

        // ========================================
        // 7. Admin Notification History - NOTI-09
        // ========================================
        services.AddScoped<
            INotificationHistoryService,
            NotificationHistoryService>();

        // ========================================
        // 8. Admin Event Logs - NOTI-13, NOTI-14
        // ========================================
        services.AddScoped<
            IEventLogService,
            EventLogService>();

        // ========================================
        // 9. RabbitMQ Broker Health - NOTI-16
        // ========================================
        services.AddScoped<
            IBrokerHealthService,
            BrokerHealthService>();

        return services;
    }
}
