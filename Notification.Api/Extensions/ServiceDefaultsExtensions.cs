namespace Notification.Api.Extensions;

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Notification.Api.Common.Converters;
using Notification.Api.Middleware;

public static class ServiceDefaultsExtensions
{
    public static IServiceCollection AddServiceDefaults(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        services.AddServiceObservability(configuration, serviceName);
        services.AddNotificationAuthentication(configuration);
        services.AddNotificationSwagger();

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new PascalCaseJsonStringEnumConverter(allowIntegerValues: false));
        });
        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new PascalCaseJsonStringEnumConverter(allowIntegerValues: false));
        });

        return services;
    }

    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseGlobalExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.UseNotificationSwagger();
        }

        return app;
    }
}
