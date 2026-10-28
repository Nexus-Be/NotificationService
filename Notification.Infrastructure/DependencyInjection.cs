namespace Notification.Infrastructure;

using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notification.Application.Consumers;
using Notification.Application.Interfaces;
using Notification.Infrastructure.Persistence;
using Notification.Infrastructure.Services.Email;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var rawConnection = configuration.GetConnectionString("DefaultConnection")
            ?? configuration["CONNECTION_STRING"]
            ?? configuration["NOTIFICATION_CONNECTION_STRING"]
            ?? configuration["NOTIFICATION_DB_CONNECTION"]
            ?? "Host=localhost;Port=5439;Database=notification_db;Username=postgres;Password=postgres";
        var connectionString = NormalizeConnectionString(rawConnection);

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

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
        services.AddSingleton<IEmailService, MailKitEmailSenderService>();
        services.AddSingleton<IEmailSenderService>(sp => (MailKitEmailSenderService)sp.GetRequiredService<IEmailService>());
        services.AddSingleton<Notification.Application.Common.Interfaces.IEmailSenderService>(sp => (MailKitEmailSenderService)sp.GetRequiredService<IEmailService>());

        // MassTransit RabbitMQ Consumer
        services.AddMassTransit(x =>
        {
            x.AddApplicationConsumers();

            x.SetKebabCaseEndpointNameFormatter();

            x.AddEntityFrameworkOutbox<AppDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                var host = configuration["RabbitMq:Host"] ?? "localhost";
                var portStr = configuration["RabbitMq:Port"] ?? "5672";
                var port = ushort.TryParse(portStr, out var p) ? p : (ushort)5672;
                var user = configuration["RabbitMq:User"] ?? "guest";
                var pass = configuration["RabbitMq:Password"] ?? "guest";

                cfg.Host(host, port, "/", h =>
                {
                    h.Username(user);
                    h.Password(pass);
                });

                cfg.UseMessageRetry(r =>
                {
                    r.Handle<InvalidOperationException>();
                    r.Intervals(
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(10)
                    );
                });
                cfg.ConfigureEndpoints(context);
            });
        });

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
