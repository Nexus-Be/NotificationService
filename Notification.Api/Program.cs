using Microsoft.EntityFrameworkCore;
using Notification.Api.Extensions;
using Notification.Application;
using Notification.Infrastructure;
using Notification.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// 1. Auxiliary Service Defaults (Observability, Auth, Swagger, JSON options)
builder.Services.AddServiceDefaults(builder.Configuration, "notification-service");

// 2. Application & Infrastructure Dependency Injection
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// 3. REST Services
builder.Services.AddControllers();

var app = builder.Build();

// 4. Default Middleware Pipeline (Exception Handler, Auth, Swagger UI)
app.UseServiceDefaults();

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

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "notification-service" }));

app.Run();
