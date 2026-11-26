namespace Notification.Api.Extensions;

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

public static class WalletNotificationAuth
{
    public const string Scheme = "WalletNotifications";
    public const string Policy = "WalletNotificationWriter";
    public const string DepositPolicy = "AuctionOrWalletDepositWriter";
    public const string CallerClaim = "notification-caller";

    public static void AddWalletNotificationAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, WalletNotificationHandler>(Scheme, _ => { });
        var auctionId = configuration["AuctionNotificationAuth:ServiceUserId"] ?? "00000000-0000-0000-0000-0000000000a1";
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policy, policy => policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser()
                .RequireClaim(CallerClaim, "wallet"));
            options.AddPolicy(DepositPolicy, policy => policy.AddAuthenticationSchemes(AuctionNotificationAuth.Scheme, Scheme)
                .RequireAuthenticatedUser().RequireAssertion(context => context.User.HasClaim(CallerClaim, "wallet") ||
                    context.User.HasClaim("sub", auctionId) && context.User.HasClaim("scope", "auction.notifications.write")));
        });
    }
}

public sealed class WalletNotificationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var path = Request.Path.Value?.TrimEnd('/');
        if (!HttpMethods.IsPost(Request.Method) || path is null ||
            !(path.Equals("/api/v1/notifications/withdrawal-events", StringComparison.OrdinalIgnoreCase) ||
              path.Equals("/api/v1/notifications/auction-deposits", StringComparison.OrdinalIgnoreCase)) ||
            !Request.Headers.TryGetValue("X-Internal-Service-Key", out var supplied))
            return Task.FromResult(AuthenticateResult.NoResult());
        var key = configuration["WalletNotificationAuth:ServiceKey"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32 || supplied.Count != 1 ||
            Request.Headers.ContainsKey("Authorization") ||
            new[] { "x-user-id", "x-user-roles", "x-user-role", "x-user-email", "x-user-permissions" }.Any(Request.Headers.ContainsKey) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(supplied.ToString())))
            return Task.FromResult(AuthenticateResult.Fail("Wallet service authentication failed."));
        var identity = new ClaimsIdentity(new[] { new Claim(WalletNotificationAuth.CallerClaim, "wallet") }, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
