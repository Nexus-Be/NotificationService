namespace Notification.Api.Middleware;

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public sealed class InternalServiceAuthMiddleware(
    RequestDelegate next,
    IConfiguration configuration,
    ILogger<InternalServiceAuthMiddleware> logger)
{
    public const string HeaderKeyInternalService = "X-Internal-Service-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1/internal", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var configuredKey = configuration["INTERNAL_SERVICE_SECRET"] ?? configuration["InternalSecurity:ServiceKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            logger.LogError("Internal service key is not configured.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                errorCode = "INTERNAL_CONFIGURATION_ERROR",
                message = "Internal service authentication is not configured."
            });
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderKeyInternalService, out var provided) ||
            provided.Count != 1 ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided.ToString()), Encoding.UTF8.GetBytes(configuredKey)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                errorCode = "FORBIDDEN",
                message = "Access denied to internal service resource."
            });
            return;
        }

        await next(context);
    }
}
