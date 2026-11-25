namespace Notification.Api.Filters;

using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Lets a backend service call a route with the shared internal key (<c>X-Internal-Service-Key</c>), the way Wallet does, and otherwise
/// falls back to the usual role check (for example an admin). Without this, a service that has no user identity to forward is refused with 401.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireServiceKeyOrRoleAttribute : Attribute, IAuthorizationFilter
{
    public const string HeaderName = "X-Internal-Service-Key";

    private readonly RequireRoleAttribute _roles;

    public RequireServiceKeyOrRoleAttribute(params string[] allowedRoles)
    {
        _roles = new RequireRoleAttribute(allowedRoles);
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var configuration = context.HttpContext.RequestServices.GetService<IConfiguration>();
        var configuredKey = configuration?["INTERNAL_SERVICE_SECRET"] ?? configuration?["InternalSecurity:ServiceKey"];

        if (!string.IsNullOrWhiteSpace(configuredKey) &&
            context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided) &&
            provided.Count == 1 &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided.ToString()), Encoding.UTF8.GetBytes(configuredKey)))
        {
            return;
        }

        _roles.OnAuthorization(context);
    }
}
