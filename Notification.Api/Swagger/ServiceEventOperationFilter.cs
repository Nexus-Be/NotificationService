namespace Notification.Api.Swagger;

using Microsoft.OpenApi.Models;
using Notification.Api.Controllers.Public;
using Notification.Application.Services.AuctionActivity;
using Notification.Application.Services.WalletEvents;
using Swashbuckle.AspNetCore.SwaggerGen;

public sealed class ServiceEventOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType == typeof(AuctionDepositsController))
        {
            foreach (var media in operation.RequestBody.Content.Values)
                media.Schema = new OpenApiSchema { OneOf = new List<OpenApiSchema>
                {
                    context.SchemaGenerator.GenerateSchema(typeof(AuctionDepositRequest), context.SchemaRepository),
                    context.SchemaGenerator.GenerateSchema(typeof(WalletDepositEventRequest), context.SchemaRepository)
                } };
            operation.Description = "Auction JWT selects AuctionDepositRequest. The Wallet service key selects WalletDepositEventRequest. Mixed contracts are rejected.";
            operation.Security = new List<OpenApiSecurityRequirement> { Security("Bearer"), Security("WalletServiceKey") };
        }
        else if (context.MethodInfo.DeclaringType == typeof(WithdrawalEventsController) ||
                 context.MethodInfo.DeclaringType == typeof(WalletTopupsController))
            operation.Security = new List<OpenApiSecurityRequirement> { Security("WalletServiceKey") };
    }

    private static OpenApiSecurityRequirement Security(string id) => new()
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = id } }] = Array.Empty<string>()
    };
}
