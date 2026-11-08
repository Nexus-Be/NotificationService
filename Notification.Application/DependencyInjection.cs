namespace Notification.Application;

using Microsoft.Extensions.DependencyInjection;
using Notification.Application.Services.Email;
using Notification.Application.Services.Health;
using Notification.Application.Services.EventLogs;
using Notification.Application.Services.Dispatches;
using Notification.Application.Services.Deliveries;
using Notification.Application.Services.Management;
using Notification.Application.Services.Notifications;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<Notification.Application.Services.PasswordChanges.IPasswordChangeService, Notification.Application.Services.PasswordChanges.PasswordChangeService>();
        services.AddScoped<Notification.Application.Services.SellerDecisions.ISellerDecisionService, Notification.Application.Services.SellerDecisions.SellerDecisionService>();
        services.AddScoped<Notification.Application.Services.AccountStatusChanges.IAccountStatusChangeService, Notification.Application.Services.AccountStatusChanges.AccountStatusChangeService>();
        services.AddScoped<Notification.Application.Services.OrderPayments.IOrderPaymentService, Notification.Application.Services.OrderPayments.OrderPaymentService>();
        services.AddScoped<Notification.Application.Services.AuctionOutbids.IAuctionOutbidService, Notification.Application.Services.AuctionOutbids.AuctionOutbidService>();
        services.AddScoped<Notification.Application.Services.AuctionWinners.IAuctionWinnerService, Notification.Application.Services.AuctionWinners.AuctionWinnerService>();
        services.AddScoped<IOtpEmailDispatchService, OtpEmailDispatchService>();
        services.AddScoped<IHealthService, HealthService>();
        services.AddScoped<IEventLogRetentionService, EventLogRetentionService>();
        services.AddScoped<IEventLogService, EventLogService>();
        services.AddScoped<INotificationManagementService, NotificationManagementService>();
        services.AddScoped<IDeliveryService, DeliveryService>();
        services.AddScoped<IDispatchService, DispatchService>();
        services.AddScoped<IEmailNotificationService, EmailNotificationService>();
        services.AddScoped<IUserNotificationService, UserNotificationService>();
        return services;
    }
}
