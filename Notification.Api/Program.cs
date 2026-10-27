using Microsoft.EntityFrameworkCore;
using Notification.Api.Middleware;
using Notification.Application;
using Notification.Infrastructure;
using Notification.Infrastructure.Persistence;
using Notification.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 0. Observability / OpenTelemetry Tracing
builder.Services.AddServiceObservability(builder.Configuration, "notification-service");

// 1. Application & Infrastructure Dependency Injection
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddNotificationAuthentication(builder.Configuration);

// 2. Swagger / OpenAPI & JSON Serialization
builder.Services.AddControllers();
builder.Services.AddNotificationSwagger();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new Notification.Api.Common.Converters.PascalCaseJsonStringEnumConverter(allowIntegerValues: false));
});
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.Converters.Add(new Notification.Api.Common.Converters.PascalCaseJsonStringEnumConverter(allowIntegerValues: false));
});

var app = builder.Build();

// 3. Global Exception Handler Middleware
app.UseGlobalExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("EnableSwagger", true))
{
    app.UseNotificationSwagger();
}

// Automatically apply database migrations on startup if relational DB is available
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        if (db.Database.IsRelational())
        {
            db.Database.Migrate();
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning("Database migration deferred on startup: {Message}", ex.Message);
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Notification.Api" }));

app.MapControllers();

app.Run();
