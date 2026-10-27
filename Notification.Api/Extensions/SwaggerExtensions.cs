namespace Notification.Api.Extensions;

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

public static class SwaggerExtensions
{
    public static IServiceCollection AddNotificationSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Nexus Notification Service REST API",
                Version = "v1",
                Description = """
                    RESTful API cho Notification Service (Port 8007).

                    Các tính năng độc lập hỗ trợ:
                    - Email: Gửi OTP xác thực đăng ký (/api/v1/notifications/email/otp) và gửi email tùy biến (/api/v1/notifications/email/send).
                    - Device Token: Đăng ký & thu hồi FCM/APNS token (/api/v1/notifications/devices).
                    - Preferences: Quản lý tùy chọn nhận thông báo của người dùng (/api/v1/notifications/preferences).
                    - In-App Notifications: Xem danh sách thông báo và đánh dấu đã đọc (/api/v1/notifications/me).
                    - Admin Monitoring: Giám sát trạng thái broker & hàng đợi (/api/v1/notifications/admin/monitoring).

                    Hướng dẫn kiểm thử xác thực:
                    - Bấm nút 'Authorize' 🔓 bên dưới để nhập JWT Bearer token hoặc nhập header 'x-user-id' (hệ thống tự động fallback demo GUID khi test trực tiếp).
                    """
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Nhập JWT Bearer token để xác thực các endpoint yêu cầu quyền người dùng."
            });

            options.AddSecurityDefinition("x-user-id", new OpenApiSecurityScheme
            {
                Name = "x-user-id",
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "Header x-user-id giả lập truyền từ API Gateway (định dạng GUID)."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                },
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "x-user-id"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }

    public static WebApplication UseNotificationSwagger(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Nexus Notification API v1");
            options.RoutePrefix = "swagger";
            options.DisplayRequestDuration();
            options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
        });

        // Tự động chuyển hướng từ root "/" sang "/swagger" để tiện mở trên trình duyệt
        app.MapGet("/", () => Results.Redirect("/swagger"));

        return app;
    }
}
