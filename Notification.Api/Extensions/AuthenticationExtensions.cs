namespace Notification.Api.Extensions;

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddNotificationAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var issuer = configuration["Jwt:Issuer"] ?? configuration["JWT_ISSUER"] ?? "http://localhost:8080";
        var audience = configuration["Jwt:Audience"] ?? configuration["JWT_AUDIENCE"] ?? "shoppingcart-api";
        var signature = configuration["Jwt:Signature"] ?? configuration["JWT_SIGNATURE"] ?? "$d)aam<_ze0E0bGB/5b7YE|^(sxQF_V(,dZusL*?$G{aqgXd&$M#+wd(R+<JN%eZ";

        if (string.IsNullOrWhiteSpace(issuer)
            || string.IsNullOrWhiteSpace(audience)
            || string.IsNullOrWhiteSpace(signature)
            || Encoding.UTF8.GetByteCount(signature) < 32)
        {
            throw new InvalidOperationException(
                "JWT issuer, audience, and a signing key of at least 32 bytes are required.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(signature)),
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorization();
        return services;
    }
}