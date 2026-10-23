using MassTransit;
using Notification.Application.Common.Interfaces;
using Notification.Infrastructure.Consumers;
using Notification.Infrastructure.Email;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình SmtpOptions & EmailSenderService
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.AddSingleton<IEmailSenderService, MailKitEmailSenderService>();

// 2. Cấu hình MassTransit RabbitMQ Consumer
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<SendOtpEmailConsumer>();

    x.SetKebabCaseEndpointNameFormatter();

    x.UsingRabbitMq((context, cfg) =>
    {
        var host = builder.Configuration["RabbitMq:Host"] ?? "localhost";
        var portStr = builder.Configuration["RabbitMq:Port"] ?? "5672";
        var port = ushort.TryParse(portStr, out var p) ? p : (ushort)5672;
        var user = builder.Configuration["RabbitMq:User"] ?? "guest";
        var pass = builder.Configuration["RabbitMq:Password"] ?? "guest";

        cfg.Host(host, port, "/", h =>
        {
            h.Username(user);
            h.Password(pass);
        });

        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Notification.Api" }));

app.Run();
