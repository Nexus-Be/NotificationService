namespace Notification.Api.Extensions;

using Notification.Application.Services.Health;

public static class HealthEndpointExtensions
{
    public static IEndpointRouteBuilder MapNotificationHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new LivenessResponse());
        }).AllowAnonymous().WithName("NotificationLiveness").WithTags("Health").Produces<LivenessResponse>(200);
        endpoints.MapGet("/health/ready", async (IHealthService service, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var result = await service.CheckAsync(ct);
            return Results.Json(result.ToReadiness(),statusCode: result.IsReady ? 200 : 503);
        }).AllowAnonymous().WithName("NotificationReadiness").WithTags("Health")
            .Produces<ReadinessResponse>(200).Produces<ReadinessResponse>(503);
        return endpoints;
    }
}
