namespace Notification.Api.Extensions;

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

internal static class AuctionNotificationAuth
{
    public const string Scheme = "AuctionNotifications";
    public const string Policy = "AuctionNotificationWriter";

    public static void AddAuctionNotificationAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var signingKey = configuration["AuctionNotificationAuth:SigningKey"];
        var serviceId = configuration["AuctionNotificationAuth:ServiceUserId"]
            ?? "00000000-0000-0000-0000-0000000000a1";

        services.AddAuthentication().AddJwtBearer(Scheme, options =>
        {
            options.MapInboundClaims = false;
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var path = context.Request.Path.Value?.TrimEnd('/');
                    if (!HttpMethods.IsPost(context.Request.Method) ||
                        !new[] { "/api/v1/notifications/auction-failed", "/api/v1/notifications/auction-cancelled",
                            "/api/v1/notifications/auction-winner-offers" }.Contains(path, StringComparer.OrdinalIgnoreCase))
                        context.NoResult();
                    return Task.CompletedTask;
                }
            };
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = "auction-service",
                ValidAudience = "nexus-auction-notifications",
                ValidateIssuerSigningKey = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                IssuerSigningKey = string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32
                    ? null : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                ClockSkew = TimeSpan.FromSeconds(15),
                ValidateLifetime = true
            };
        });
        services.AddAuthorization(options => options.AddPolicy(Policy, policy =>
        {
            policy.AddAuthenticationSchemes(Scheme);
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("sub", serviceId);
            policy.RequireClaim("scope", "auction.notifications.write");
        }));
    }
}
