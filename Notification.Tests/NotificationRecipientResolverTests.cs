namespace Notification.Tests;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Notification.Infrastructure.Services.Recipients;
using Notification.Api.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
public sealed class NotificationRecipientResolverTests
{
    private const string Key="notification-contact-test-key-over-32-bytes";
    [Fact]public async Task ResolverUsesDedicatedCredential_ValidatesRecipientIdentity(){
        var user=Guid.NewGuid();var handler=new Handler{Body=new{userId=user,email="verified@example.com"}};
        var resolver=Resolver(handler);Assert.Equal("verified@example.com",await resolver.ResolveAsync(user));
        Assert.Equal($"/api/v1/users/{user}/notification-contacts",handler.Path);Assert.Equal(Key,handler.Key);Assert.False(handler.HasGenericKey);
    }
    [Fact]public async Task ResolverRejectsWrongUser(){var handler=new Handler{Body=new{userId=Guid.NewGuid(),email="other@example.com"}};await Assert.ThrowsAsync<HttpRequestException>(()=>Resolver(handler).ResolveAsync(Guid.NewGuid()));}
    [Fact]public async Task MissingVerifiedContactIsNotDeliverySuccess(){Assert.Null(await Resolver(new Handler{Status=HttpStatusCode.NotFound}).ResolveAsync(Guid.NewGuid()));}
    [Fact]public async Task UnavailableContactLookupIsRetriedByWorker(){await Assert.ThrowsAsync<HttpRequestException>(()=>Resolver(new Handler{Status=HttpStatusCode.ServiceUnavailable}).ResolveAsync(Guid.NewGuid()));}
    [Theory][InlineData(null,false)][InlineData("wrong",false)][InlineData(Key,true)]
    public void EmailFilterRequiresValidStrongKey_WithoutTrustingNetwork(string? supplied,bool allowed){
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["InternalSecurity:ServiceKey"]=Key}).Build();
        var http=new DefaultHttpContext{RequestServices=new ServiceCollection().AddSingleton<IConfiguration>(config).BuildServiceProvider()};
        if(supplied is not null)http.Request.Headers["X-Internal-Service-Key"]=supplied;
        var filter=new AuthorizationFilterContext(new ActionContext(http,new RouteData(),new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),new List<IFilterMetadata>());
        new RequireServiceKeyOrRoleAttribute("service","admin").OnAuthorization(filter);
        if(allowed)Assert.Null(filter.Result);else Assert.Equal(401,Assert.IsType<ObjectResult>(filter.Result).StatusCode);
    }
    private static UserNotificationRecipientResolver Resolver(Handler handler)=>new(new HttpClient(handler){BaseAddress=new Uri("http://users.test/")},new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Services:User:NotificationContactKey"]=Key}).Build());
    private sealed class Handler:HttpMessageHandler{
        public object? Body;public HttpStatusCode Status=HttpStatusCode.OK;public string? Path,Key;public bool HasGenericKey;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Path=r.RequestUri!.AbsolutePath;Key=r.Headers.GetValues("X-Notification-Contact-Key").Single();HasGenericKey=r.Headers.Contains("X-Internal-Service-Key");return Task.FromResult(new HttpResponseMessage(Status){Content=JsonContent.Create(Body)});}
    }
}
