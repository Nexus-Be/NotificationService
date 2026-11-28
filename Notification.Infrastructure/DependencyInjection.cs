namespace Notification.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Notification.Application.Services.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notification.Application.Interfaces;
using Notification.Infrastructure.BackgroundWorkers;
using Notification.Infrastructure.Persistence;
using Notification.Infrastructure.Services.Email;
using Notification.Infrastructure.Services.Push;
using Notification.Application.Services.Deliveries;
using Notification.Application.Services.EventLogs;
using Notification.Application.Services.Health;
using Notification.Infrastructure.Health;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var rawConnection = configuration.GetConnectionString("DefaultConnection")
            ?? configuration["CONNECTION_STRING"]
            ?? configuration["NOTIFICATION_CONNECTION_STRING"]
            ?? configuration["NOTIFICATION_DB_CONNECTION"]
            ?? Environment.GetEnvironmentVariable("NOTIFICATION_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(rawConnection) || rawConnection.Contains("REPLACE_WITH"))
        {
            throw new InvalidOperationException("Database connection string for NotificationService is not configured. Set 'ConnectionStrings:DefaultConnection' or environment variable 'NOTIFICATION_CONNECTION_STRING'.");
        }
        var connectionString = NormalizeConnectionString(rawConnection);

        services.AddScoped<NotificationRoutingInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            if (configuration.GetValue("NotificationRouting:Enabled", true))
                options.AddInterceptors(sp.GetRequiredService<NotificationRoutingInterceptor>());
        });
        services.AddHttpClient<INotificationRecipientResolver, Notification.Infrastructure.Services.Recipients.UserNotificationRecipientResolver>(client =>
        {
            var url = configuration["USER_SERVICE_URL"] ?? configuration["Services:User:BaseUrl"] ?? "http://user-service:8001/";
            client.BaseAddress = new Uri(url.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(8);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        services.AddScoped<INotificationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Email Infrastructure Service
        services.Configure<SmtpOptions>(opts =>
        {
            configuration.GetSection(SmtpOptions.SectionName).Bind(opts);

            // Fallback / Override from flat environment variables if provided
            var envHost = configuration["SMTP_HOST"];
            if (!string.IsNullOrWhiteSpace(envHost)) opts.Host = envHost;

            var envPort = configuration["SMTP_PORT"];
            if (int.TryParse(envPort, out var p)) opts.Port = p;

            var envSsl = configuration["SMTP_USE_SSL"];
            if (bool.TryParse(envSsl, out var ssl)) opts.UseSsl = ssl;

            var envSenderName = configuration["SMTP_SENDER_NAME"];
            if (!string.IsNullOrWhiteSpace(envSenderName)) opts.SenderName = envSenderName;

            var envSenderEmail = configuration["SMTP_SENDER_EMAIL"];
            if (!string.IsNullOrWhiteSpace(envSenderEmail)) opts.SenderEmail = envSenderEmail;

            var envUsername = configuration["SMTP_USERNAME"];
            if (!string.IsNullOrWhiteSpace(envUsername)) opts.Username = envUsername;

            var envPassword = configuration["SMTP_PASSWORD"];
            if (!string.IsNullOrWhiteSpace(envPassword)) opts.Password = envPassword;
        });
        services.Configure<ResendOptions>(opts =>
        {
            configuration.GetSection(ResendOptions.SectionName).Bind(opts);
            if (!string.IsNullOrWhiteSpace(configuration["RESEND_API_KEY"])) opts.ApiKey = configuration["RESEND_API_KEY"]!;
            if (!string.IsNullOrWhiteSpace(configuration["RESEND_FROM_EMAIL"])) opts.FromEmail = configuration["RESEND_FROM_EMAIL"]!;
            if (!string.IsNullOrWhiteSpace(configuration["RESEND_FROM_NAME"])) opts.FromName = configuration["RESEND_FROM_NAME"]!;
        });
        var emailProvider = configuration["EmailProvider"] ?? "Smtp";
        if (emailProvider.Equals("Resend", StringComparison.OrdinalIgnoreCase))
        {
            var resendKey = configuration["RESEND_API_KEY"] ?? configuration["Resend:ApiKey"];
            var resendFrom = configuration["RESEND_FROM_EMAIL"] ?? configuration["Resend:FromEmail"];
            if (string.IsNullOrWhiteSpace(resendKey) || string.IsNullOrWhiteSpace(resendFrom))
                throw new InvalidOperationException("Resend API key and sender email must be configured.");
            var resendBase = configuration["Resend:BaseUrl"] ?? "https://api.resend.com/";
            if (!Uri.TryCreate(resendBase, UriKind.Absolute, out var resendUri) || resendUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Resend base URL must use HTTPS.");
            services.AddHttpClient<IEmailDeliverySender, ResendEmailDeliverySender>(client =>
            {
                client.BaseAddress = resendUri;
                client.Timeout = Timeout.InfiniteTimeSpan;
            });
        }
        else if (emailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IEmailDeliverySender, SmtpEmailDeliverySender>();
        else
            throw new InvalidOperationException("Email provider must be Smtp or Resend.");
        services.AddTransient<IEmailService, MailKitEmailSenderService>();
        services.AddTransient<IEmailSenderService>(sp => (MailKitEmailSenderService)sp.GetRequiredService<IEmailService>());

        var deliverySettings = new DeliveryWorkerSettings();
        configuration.GetSection("DeliveryWorker").Bind(deliverySettings);
        var configuredAttemptLimit = configuration["NOTIFICATION_RETRY_LIMIT"];
        if (!string.IsNullOrWhiteSpace(configuredAttemptLimit))
        {
            if (!int.TryParse(configuredAttemptLimit, out var attemptLimit))
                throw new InvalidOperationException("Notification retry limit must be a valid integer.");
            deliverySettings.MaxAttempts = attemptLimit;
        }
        deliverySettings.Validate();
        services.AddSingleton(deliverySettings);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        var fcmOptions = new FcmOptions();
        configuration.GetSection("Fcm").Bind(fcmOptions);
        if (!string.IsNullOrWhiteSpace(configuration["FCM_PROJECT_ID"])) fcmOptions.ProjectId = configuration["FCM_PROJECT_ID"]!;
        services.AddSingleton(fcmOptions);
        services.AddSingleton<IFcmAccessTokenProvider, GoogleFcmAccessTokenProvider>();
        services.AddHttpClient<IPushDeliverySender, FcmPushDeliverySender>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddHostedService<DatabaseEmailWorker>();

        var retentionSettings = new EventLogRetentionSettings();
        configuration.GetSection("EventLogRetention").Bind(retentionSettings);
        retentionSettings.Validate();
        services.AddSingleton(retentionSettings);
        services.AddHostedService<EventLogRetentionWorker>();

        var healthSettings = new HealthSettings();
        configuration.GetSection("Health").Bind(healthSettings);
        healthSettings.Validate();
        services.AddSingleton(healthSettings);
        services.AddSingleton<WorkerHealthRegistry>();
        services.AddSingleton<IWorkerHealthReporter>(sp => sp.GetRequiredService<WorkerHealthRegistry>());
        services.AddSingleton<IWorkerHealthReader>(sp => sp.GetRequiredService<WorkerHealthRegistry>());
        services.AddScoped<IDatabaseHealthProbe, DatabaseHealthProbe>();

        var otpSettings = new OtpWorkerSettings();
        configuration.GetSection("OtpWorker").Bind(otpSettings);
        otpSettings.Validate();
        services.AddSingleton(otpSettings);
        services.AddOtpDataProtection(configuration);
        services.AddSingleton<IOtpPayloadProtector, DataProtectionOtpPayloadProtector>();
        services.AddHostedService<DurableOtpWorker>();

        // Identity propagation delegating handler for outgoing inter-service HTTP requests
        services.AddTransient<Services.Common.IdentityPropagationHandler>();

        return services;
    }

    private static string NormalizeConnectionString(string? connStr)
    {
        if (string.IsNullOrWhiteSpace(connStr)) return string.Empty;
        if (connStr.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || 
            connStr.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(connStr);
            var userInfo = uri.UserInfo.Split(':');
            var user = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
            var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var host = uri.Host;
            var port = uri.Port > 0 ? uri.Port : 5432;
            var db = uri.AbsolutePath.TrimStart('/');
            return $"Host={host};Port={port};Database={db};Username={user};Password={pass};SSL Mode=Require;Trust Server Certificate=true";
        }
        return connStr;
    }
}
