namespace Notification.Api.Middleware;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

/// <summary>Validates identity at a public production ingress before trusted header extraction.</summary>
public sealed class PublicRequestIdentityMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private static readonly string[] IdentityHeaders =
    ["x-user-id", "x-user-roles", "x-user-role", "x-user-email", "x-user-permissions", "x-user-permission", "x-session-id"];

    public async Task InvokeAsync(HttpContext context)
    {
        var hasForwardedIdentity = IdentityHeaders.Any(context.Request.Headers.ContainsKey);
        if (hasForwardedIdentity && HasBackendKey(context.Request))
        {
            await next(context);
            return;
        }

        // Remove every fallback source before UserContextMiddleware and HttpContextExtensions can read it.
        foreach (var header in IdentityHeaders) context.Request.Headers.Remove(header);
        context.Request.Headers.Remove("X-Caller-Service");

        var authorization = context.Request.Headers.Authorization;
        var hasBearer = authorization.Count == 1 &&
            authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        if (hasForwardedIdentity && !hasBearer)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                errorCode = "UNTRUSTED_FORWARDED_IDENTITY",
                message = "Forwarded identity requires valid service authentication."
            });
            return;
        }

        if (hasBearer)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            if (TryAuthenticateUser(authorization.ToString()[7..].Trim(), out var principal))
                context.User = principal!;
            // Scoped Auction JWTs and Wallet credentials remain subject to their endpoint authentication schemes.
            // An invalid user token never restores the removed identity headers.
        }

        await next(context);
    }

    private bool HasBackendKey(HttpRequest request)
    {
        var expected = configuration["INTERNAL_SERVICE_SECRET"] ?? configuration["InternalSecurity:ServiceKey"];
        return !string.IsNullOrWhiteSpace(expected) && Encoding.UTF8.GetByteCount(expected) >= 32 &&
            request.Headers.TryGetValue("X-Internal-Service-Key", out var supplied) && supplied.Count == 1 &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied.ToString()));
    }

    private bool TryAuthenticateUser(string token, out ClaimsPrincipal? principal)
    {
        principal = null;
        var key = configuration["Jwt:Signature"] ?? configuration["JWT_SIGNATURE"];
        var issuer = configuration["Jwt:Issuer"] ?? configuration["JWT_ISSUER"];
        var audience = configuration["Jwt:Audience"] ?? configuration["JWT_AUDIENCE"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32 ||
            string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience)) return false;
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var validated = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateIssuer = true, ValidIssuer = issuer,
                ValidateAudience = true, ValidAudience = audience,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.FromSeconds(15)
            }, out _);
            var subject = validated.FindFirst("sub")?.Value ?? validated.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(subject, out var userId) || userId == Guid.Empty) return false;
            var identity = new ClaimsIdentity(validated.Claims, "ValidatedUserJwt");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
            foreach (var role in validated.FindAll("role").Concat(validated.FindAll("roles"))
                         .Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase))
                identity.AddClaim(new Claim(ClaimTypes.Role, role.ToLowerInvariant()));
            principal = new ClaimsPrincipal(identity);
            return true;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return false;
        }
    }
}
