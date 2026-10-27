using Microsoft.EntityFrameworkCore;
using Notification.Api.Middleware;
using Notification.Application;
using Notification.Infrastructure;
using Notification.Infrastructure.Persistence;
using Notification.Api.Extensions;
using Notification.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Application & Infrastructure Dependency Injection
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddGrpc();
builder.Services.AddNotificationAuthentication(builder.Configuration);

// 2. Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var app = builder.Build();

// 3. Global Exception Handler Middleware
app.UseGlobalExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<NotificationGrpcService>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
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

app.Run();
