namespace Notification.Api.Extensions;

using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

public static class NotificationHubAuth
{
    public const string Scheme = "NotificationUsers";
    public const string Policy = "NotificationUserHub";

    public static IServiceCollection AddNotificationHubAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var key = configuration["Jwt:Signature"] ?? configuration["JWT_SIGNATURE"];
        var issuer = configuration["Jwt:Issuer"] ?? configuration["JWT_ISSUER"];
        var audience = configuration["Jwt:Audience"] ?? configuration["JWT_AUDIENCE"];
        services.AddAuthentication().AddJwtBearer(Scheme, options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32
                    ? null : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateIssuer = true, ValidIssuer = issuer,
                ValidateAudience = true, ValidAudience = audience,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.FromSeconds(15)
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (!context.Request.Path.StartsWithSegments("/hubs/notifications"))
                    {
                        context.NoResult();
                        return Task.CompletedTask;
                    }
                    if (context.Request.Query.TryGetValue("access_token", out var values) && values.Count == 1)
                        context.Token = values.ToString();
                    return Task.CompletedTask;
                }
            };
        });
        services.AddAuthorization(options => options.AddPolicy(Policy, policy =>
        {
            policy.AddAuthenticationSchemes(Scheme);
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context => Guid.TryParse(context.User.FindFirst("sub")?.Value,
                out var id) && id != Guid.Empty);
        }));
        services.AddSingleton<IUserIdProvider, NotificationHubUserIdProvider>();
        return services;
    }

    private sealed class NotificationHubUserIdProvider : IUserIdProvider
    {
        public string? GetUserId(HubConnectionContext connection) =>
            connection.User?.FindFirst("sub")?.Value;
    }
}
