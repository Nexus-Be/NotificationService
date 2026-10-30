namespace Notification.Api.Extensions;

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

public static class SwaggerExtensions
{
    public static IServiceCollection AddNotificationSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Nexus Notification Service REST API",
                Version = "v1",
                Description = """
                    RESTful API for Notification Service (Port 8007).

                    Supported standalone features:
                    - Email: Send registration verification OTP (/api/v1/notifications/email/otp) and custom emails.
                    - Device Token: Register and revoke FCM/APNS tokens (/api/v1/notifications/devices).
                    - Preferences: Manage user notification preferences (/api/v1/notifications/preferences).
                    - In-App Notifications: View notification history and mark as read (/api/v1/notifications/me).
                    - Admin Monitoring: Monitor message broker health and queues (/api/v1/admin/health/broker).
                    """
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Enter a JWT Bearer token to authenticate requests requiring user permissions."
            });

            options.AddSecurityDefinition("x-user-id", new OpenApiSecurityScheme
            {
                Name = "x-user-id",
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "Header x-user-id forwarded from API Gateway (GUID format)."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                },
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "x-user-id"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }

    public static WebApplication UseNotificationSwagger(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Nexus Notification API v1");
            options.RoutePrefix = "swagger";
            options.DisplayRequestDuration();
            options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
        });

        // Automatically redirect from root "/" to "/swagger" for browser convenience
        app.MapGet("/", () => Results.Redirect("/swagger"));

        return app;
    }
}
