namespace Notification.Tests;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Notification.Api.Extensions;
using Notification.Api.Middleware;
using Xunit;

public sealed class PublicRequestIdentityTests
{
    private const string UserKey = "fixture-user-signature-only-64-bytes-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BackendKey = "fixture-backend-key-only-64-bytes-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid SpoofedId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static IConfiguration Config(string? key = BackendKey) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string,string?> { ["InternalSecurity:ServiceKey"]=key, ["Jwt:Signature"]=UserKey,
            ["Jwt:Issuer"]="fixture-users", ["Jwt:Audience"]="fixture-browser" }).Build();
    private static DefaultHttpContext Context(bool forwarded = true)
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/v1/notifications/me";
        if (forwarded)
        {
            context.Request.Headers["x-user-id"] = SpoofedId.ToString();
            context.Request.Headers["x-user-roles"] = "admin,service";
            context.Request.Headers["x-user-permissions"] = "ALL";
        }
        return context;
    }
    private static string Token(string issuer = "fixture-users", string audience = "fixture-browser",
        string key = UserKey, int expiresInMinutes = 5, string algorithm = SecurityAlgorithms.HmacSha256,
        string? subject = null) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer, audience, [new Claim("sub", subject ?? UserId.ToString()), new Claim("roles","buyer")],
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(expiresInMinutes),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), algorithm)));

    [Theory]
    [InlineData(null, null)]
    [InlineData(BackendKey, null)]
    [InlineData(BackendKey, "incorrect-key")]
    [InlineData("short", "short")]
    public async Task ForgedIdentityWithoutValidCredentialsIsRejected(string? configured, string? supplied)
    {
        var context=Context(); if(supplied is not null)context.Request.Headers["X-Internal-Service-Key"]=supplied;
        var called=false;
        await new PublicRequestIdentityMiddleware(_=>{called=true;return Task.CompletedTask;},Config(configured)).InvokeAsync(context);
        Assert.Equal(403,context.Response.StatusCode);Assert.False(called);Assert.Null(context.GetUserId());
        Assert.False(context.Request.Headers.ContainsKey("x-user-roles"));
    }

    [Fact]
    public async Task TrustedBackendIdentityContinuesThroughExistingExtraction()
    {
        var context=Context();context.Request.Headers["X-Internal-Service-Key"]=BackendKey;
        var called=false;
        var extraction=new UserContextMiddleware(c=>{called=true;Assert.Equal(SpoofedId,c.GetUserId());Assert.True(c.IsAdmin());return Task.CompletedTask;});
        await new PublicRequestIdentityMiddleware(extraction.InvokeAsync,Config()).InvokeAsync(context);
        Assert.True(called);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidUserTokenDeterminesIdentityInsteadOfForwardedHeaders(bool forwarded)
    {
        var context=Context(forwarded);context.Request.Headers.Authorization="Bearer "+Token();var called=false;
        var extraction=new UserContextMiddleware(c=>{called=true;Assert.Equal(UserId,c.GetUserId());Assert.True(c.HasRole("buyer"));
            Assert.False(c.IsAdmin());Assert.Empty(c.GetUserPermissions());return Task.CompletedTask;});
        await new PublicRequestIdentityMiddleware(extraction.InvokeAsync,Config()).InvokeAsync(context);
        Assert.True(called);Assert.False(context.Request.Headers.ContainsKey("x-user-id"));
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("expired")]
    [InlineData("algorithm")]
    [InlineData("subject")]
    [InlineData("malformed")]
    public async Task InvalidUserTokenNeverRestoresForgedIdentity(string failure)
    {
        var token=failure switch
        {
            "issuer"=>Token(issuer:"another-service"), "audience"=>Token(audience:"another-audience"),
            "signature"=>Token(key:BackendKey), "expired"=>Token(expiresInMinutes:-5),
            "algorithm"=>Token(algorithm:SecurityAlgorithms.HmacSha384), "subject"=>Token(subject:"not-a-user-id"),
            _=>"malformed"
        };
        var context=Context();context.Request.Headers.Authorization="Bearer "+token;var called=false;
        var extraction=new UserContextMiddleware(c=>{called=true;Assert.Null(c.GetUserId());Assert.False(c.IsAdmin());return Task.CompletedTask;});
        await new PublicRequestIdentityMiddleware(extraction.InvokeAsync,Config()).InvokeAsync(context);
        Assert.True(called);
    }

    [Fact]
    public async Task AnonymousHealthAndScopedCredentialsRemainAvailableToTheirOwnHandlers()
    {
        foreach(var path in new[]{"/health/ready","/api/v1/notifications/auction-deposits"})
        {
            var context=Context(false);context.Request.Path=path;context.Request.Headers["X-Internal-Service-Key"]="scoped-wallet-key";
            var called=false;await new PublicRequestIdentityMiddleware(c=>{called=true;Assert.Null(c.GetUserId());
                Assert.Equal("scoped-wallet-key",c.Request.Headers["X-Internal-Service-Key"]);return Task.CompletedTask;},Config()).InvokeAsync(context);
            Assert.True(called);
        }
    }

    [Fact]
    public async Task ScopedAuctionBearerIsPreservedWithoutAcceptingItsForwardedRoles()
    {
        var context=Context();var token=Token(issuer:"auction-service",audience:"nexus-auction-notifications");
        context.Request.Headers.Authorization="Bearer "+token;
        await new PublicRequestIdentityMiddleware(c=>{Assert.Null(c.GetUserId());Assert.Empty(c.GetUserRoles());
            Assert.Equal("Bearer "+token,c.Request.Headers.Authorization);return Task.CompletedTask;},Config()).InvokeAsync(context);
    }

}
