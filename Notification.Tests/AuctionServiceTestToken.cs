namespace Notification.Tests;

using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Notification.Api.Extensions;

internal static class AuctionServiceTestToken
{
    private const string SigningKey = "notification-auction-test-signing-key";
    private const string ServiceId = "00000000-0000-0000-0000-0000000000a1";

    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        configuration["AuctionNotificationAuth:SigningKey"] = SigningKey;
        configuration["AuctionNotificationAuth:ServiceUserId"] = ServiceId;
        services.AddAuctionNotificationAuth(configuration);
    }

    public static AuthenticationHeaderValue Create(string scope = "auction.notifications.write")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var token = new JwtSecurityToken("auction-service", "nexus-auction-notifications",
            new[] { new Claim("sub", ServiceId), new Claim("scope", scope) },
            expires: DateTime.UtcNow.AddMinutes(2), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }
}
