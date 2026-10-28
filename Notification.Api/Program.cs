
using Microsoft.EntityFrameworkCore;
using Notification.Api.Middleware;
using Notification.Application;
using Notification.Infrastructure;
using Notification.Infrastructure.Persistence;
using Notification.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 1. Application & Infrastructure
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// 2. RESTful API Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new Notification.Api.Common.Converters
                .PascalCaseJsonStringEnumConverter(
                    allowIntegerValues: false));
    });

// 3. JWT Authentication
builder.Services.AddNotificationAuthentication(
    builder.Configuration);

// 4. Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 5. Build Application (chỉ một lần)
var app = builder.Build();

// 6. Middleware
app.UseGlobalExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

// 7. RESTful API Routes
app.MapControllers();

// 8. Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 9. Database Migration
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    try
    {
        if (db.Database.IsRelational())
        {
            db.Database.Migrate();
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(
            "Database migration deferred on startup: {Message}",
            ex.Message);
    }
}

// 10. Health Check
app.MapGet("/health", () =>
    Results.Ok(new
    {
        status = "Healthy",
        service = "Notification.Api"
    }));

app.Run();
