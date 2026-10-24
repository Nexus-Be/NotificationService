# NOTIFICATION SERVICE — ARCHITECTURE & FOLDER STRUCTURE GUIDE
## (CẨM NANG CẤU TRÚC THƯ MỤC & PHÁT TRIỂN DÀNH CHO DEVELOPER VÀ AI AGENT)

> **Thông tin dịch vụ**:
> - **Service Port**: `8007` (HTTP/1.1 REST & HTTP/2 gRPC)
> - **Database**: PostgreSQL (`notification_db`), Port máy host: `5439`
> - **Nhiệm vụ chính**: Gửi thông báo đa kênh (In-app, Email SMTP MailKit), Quản lý mẫu thông báo (Templates), Cài đặt người dùng (Preferences), Quản lý thiết bị (Device Tokens).

---

## 1. SƠ ĐỒ PHÂN TẦNG VÀ CẤU TRÚC THƯ MỤC CHUẨN

```
src/Services/Notification/
│
├── Notification.Domain/                               # 1. TẦNG DOMAIN (POCO 100%, ZERO DEPENDENCY)
│   ├── Base/                                          # BaseEntity, IAuditable, ISoftDelete
│   ├── Entities/                                      # NotificationRecord, NotificationTemplate, DeviceToken...
│   ├── Enums/                                         # NotificationType, DeliveryStatus, ChannelType...
│   └── Exceptions/                                    # DomainException, BusinessRuleValidationException
│
├── Notification.Application/                          # 2. TẦNG APPLICATION (USE CASES & BUSINESS LOGIC)
│   ├── Interfaces/                                    # Interfaces của CSDL và các dịch vụ hạ tầng ngoài
│   │   ├── INotificationDbContext.cs                  # Abstraction của DbContext
│   │   └── IEmailSenderService.cs                     # Abstraction gửi email cho Use Case
│   │
│   ├── Common/                                        # Tiện ích và models dùng chung trong Application
│   │
│   ├── Services/                                      # CÁC DỊCH VỤ XỬ LÝ USE CASE (THEO TÍNH NĂNG)
│   │   └── Email/                                     # Module Thông báo Email
│   │       ├── IEmailNotificationService.cs           # Interface ca sử dụng gửi email
│   │       ├── EmailNotificationService.cs            # Xử lý nghiệp vụ gửi email
│   │       └── EmailNotificationDtos.cs               # SendOtpEmailDto, SendGeneralEmailDto...
│   │
│   └── DependencyInjection.cs                         # Đăng ký AddApplicationServices()
│
├── Notification.Infrastructure/                       # 3. TẦNG INFRASTRUCTURE (CÔNG NGHỆ & HẠ TẦNG CỤ THỂ)
│   ├── Persistence/                                   # Quản lý CSDL PostgreSQL & EF Core 8
│   │   ├── AppDbContext.cs                            # Triển khai INotificationDbContext
│   │   ├── Configurations/                            # 10 Fluent API Configurations (*Configuration.cs)
│   │   ├── Migrations/                                # Code-first Database Migrations
│   │   └── Seeds/                                     # Dữ liệu hạt giống kênh & mẫu thông báo
│   │
│   ├── Services/                                      # CÁC DỊCH VỤ HẠ TẦNG KỸ THUẬT (MODULE HÓA)
│   │   └── Email/                                     # Module tích hợp gửi mail SMTP
│   │       ├── IEmailService.cs                       # Interface nội bộ tầng hạ tầng
│   │       ├── SmtpOptions.cs                         # Cấu hình Options đọc từ appsettings
│   │       └── MailKitEmailSenderService.cs           # Triển khai gửi email qua MailKit
│   │
│   ├── Consumers/                                     # MassTransit RabbitMQ Consumers
│   │   └── SendOtpEmailConsumer.cs                    # Lắng nghe sự kiện SendOtpEmailIntegrationEvent
│   │
│   └── DependencyInjection.cs                         # Đăng ký AddInfrastructureServices(configuration)
│
├── Notification.Api/                                  # 4. TẦNG API / HOST (ENTRY POINT & MIDDLEWARE)
│   ├── Middleware/                                    # MIDDLEWARE TOÀN CỤC CỦA SERVICE
│   │   └── GlobalExceptionHandlerMiddleware.cs        # Bắt mọi lỗi ngoại lệ, format RFC 9110 ProblemDetails
│   ├── Contracts/                                     # shop.proto (Hợp đồng gRPC)
│   └── Program.cs                                     # Khởi động Kestrel HTTP/1 & HTTP/2, gọi DI App & Infra
│
└── Notification.Tests/                                # 5. TẦNG KIỂM THỬ (UNIT & INTEGRATION TESTS)
    └── EmailSenderTests.cs                            # Kiểm thử gửi email và chế độ simulation
```

---

## 2. NGUYÊN TẮC PHÂN CHIA TRÁCH NHIỆM

1. **`Notification.Domain`**: Chứa các Entity thông báo, kênh gửi, token thiết bị. Tuyệt đối không import thư viện ngoài hay EF Core vào đây.
2. **`Notification.Application`**: Chứa toàn bộ ca sử dụng (Use Cases). Tầng này chỉ biết đến interface `IEmailSenderService`, hoàn toàn không biết việc gửi mail bằng MailKit, SendGrid hay AWS SES.
3. **`Notification.Infrastructure`**: Triển khai `MailKitEmailSenderService` bên trong `Services/Email/`. Sau này nếu đổi sang nhà cung cấp email khác, chỉ cần sửa đúng thư mục này.
4. **`Notification.Api`**: Cổng tiếp nhận cực kỳ mỏng. Trang bị `GlobalExceptionHandlerMiddleware` để chuẩn hóa lỗi trả về cho client.

---

## 3. QUY TRÌNH THÊM TÍNH NĂNG MỚI (DÀNH CHO DEV & AI AGENT)

1. **Bước 1**: Tạo Entity & Enum trong `Notification.Domain/Entities/`.
2. **Bước 2**: Tạo Configuration trong `Notification.Infrastructure/Persistence/Configurations/` và khai báo `DbSet<T>` trong `INotificationDbContext` & `AppDbContext`.
3. **Bước 3**: Tạo thư mục tính năng trong `Notification.Application/Services/{Feature}/` gồm DTOs, Interface và Implementation.
4. **Bước 4**: Đăng ký Service vào `Notification.Application/DependencyInjection.cs`.
5. **Bước 5**: Thêm Consumer hoặc gRPC Service tương ứng.

---

## 4. QUY CHUẨN THÔNG BÁO LỖI (MANDATORY ERROR RULES)

1. **100% English**: Tuyệt đối không dùng tiếng Việt.
2. **Sentence case**: Viết hoa duy nhất chữ cái đầu (`"Notification record not found."`).
3. **Error Code SCREAMING_SNAKE_CASE**: Ví dụ `NOTIFICATION_NOT_FOUND`, `INVALID_EMAIL`, `TEMPLATE_INACTIVE`.

---

## 5. CÁCH CHẠY VÀ KIỂM THỬ ĐỘC LẬP SERVICE

```bash
# 1. Bật database của Notification Service (Port 5439):
cd src/Services/Notification
docker compose up -d notification-db

# 2. Tạo & Cập nhật migration:
./.tools/dotnet-ef migrations add InitialNotificationSchema --project Notification.Infrastructure --startup-project Notification.Api
./.tools/dotnet-ef database update --project Notification.Infrastructure --startup-project Notification.Api

# 3. Chạy service:
dotnet run --project Notification.Api

# 4. Chạy bài test:
dotnet test Notification.Tests/Notification.Tests.csproj
```
