namespace Notification.Tests;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Notification.Api.Hubs;
using Notification.Api.Extensions;
using Notification.Api.Middleware;
using Xunit;

public sealed class NotificationHubAuthTests
{
    private const string Key = "notification-hub-test-key-at-least-thirty-two-bytes-long";

    [Fact]
    public async Task Negotiate_RequiresValidUserJwt()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["Jwt:Signature"] = Key,
            ["Jwt:Issuer"] = "test-user-service",
            ["Jwt:Audience"] = "test-nexus-fe"
        });
        builder.Services.AddSignalR();
        builder.Services.AddNotificationHubAuth(builder.Configuration);
        await using var app = builder.Build();
        app.UseMiddleware<PublicRequestIdentityMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<NotificationHub>("/hubs/notifications").RequireAuthorization(NotificationHubAuth.Policy);
        await app.StartAsync();
        using var client = new HttpClient
        { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };

        using var anonymous = await client.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var token = Token();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var accepted = await client.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Contains("connectionId", await accepted.Content.ReadAsStringAsync());

        client.DefaultRequestHeaders.Authorization = null;
        using var websocketStyle = await client.PostAsync(
            "/hubs/notifications/negotiate?negotiateVersion=1&access_token=" + Uri.EscapeDataString(token), null);
        Assert.Equal(HttpStatusCode.OK, websocketStyle.StatusCode);
    }

    private static string Token()
    {
        var jwt = new JwtSecurityToken("test-user-service", "test-nexus-fe",
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", "buyer")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
