# NOTIFICATION SERVICE — ARCHITECTURE & FOLDER STRUCTURE GUIDE

> **Trạng thái triển khai (2026-10-24):** ⚠️ MỘT PHẦN — **0/4 rpc** gRPC và 0 controller (chỉ `GET /health`), **1/9 sự kiện** có consumer và consumer **không ghi DB**; nhưng schema 13 bảng + migration + seed và đường gửi email OTP qua MailKit đã chạy được.
> **Nguồn sự thật:** code (`Notification.Api/Program.cs:49`, `Notification.Infrastructure/Consumers/SendOtpEmailConsumer.cs:9`, `Notification.Infrastructure/Services/Email/MailKitEmailSenderService.cs:34-41`).
> **Cập nhật lần cuối:** 2026-10-24.

## (CẨM NANG CẤU TRÚC THƯ MỤC & PHÁT TRIỂN DÀNH CHO DEVELOPER VÀ AI AGENT)

> **Thông tin dịch vụ** (đã đối chiếu code):
> - **Service Port**: `8007` — **HTTP/1.1 only**. `Dockerfile:3-4` (`EXPOSE 8007`, `ASPNETCORE_URLS=http://+:8007`), `docker-compose.yml:37` (`8007:8007`), `Properties/launchSettings.json:17` (profile `http`). **Không có HTTP/2, không có gRPC.**
> - **Database**: PostgreSQL `notification_db`, port host `5439` (`docker-compose.yml:19`, `appsettings.json:10`).
> - **Message Broker**: RabbitMQ `5672`, vhost `/` (`appsettings.json:12-16`, `Notification.Infrastructure/DependencyInjection.cs:43-58`).
> - **Quy mô**: 53 file `.cs` / 1.324 dòng (không tính `Migrations/`); 10 entity, 11 enum, 10 DbSet, 10 configuration, 1 consumer, 2 test case.
> - **Nhiệm vụ chính (đặc tả)**: gửi thông báo đa kênh (In-app, Email SMTP MailKit), quản lý template, preference và device token. **Phần đã chạy thật chỉ là nhánh email OTP.**

---

## 1. SƠ ĐỒ PHÂN TẦNG VÀ CẤU TRÚC THƯ MỤC CHUẨN

```
src/Services/Notification/
│
├── Notification.Domain/                               # 1. TẦNG DOMAIN — 25 file .cs / 346 dòng, zero dependency
│   ├── Base/                                          # BaseEntity<TKey>, IAuditable, IUserAuditable, ISoftDelete
│   ├── Entities/                                      # 10 entity (xem bảng §2.1)
│   ├── Enums/                                         # 11 enum: NotificationType, DeliveryStatus, ChannelType...
│   └── Exceptions/                                    # DomainException, EntityNotFoundException, BusinessRuleValidationException
│
├── Notification.Application/                          # 2. TẦNG APPLICATION — 8 file .cs / 104 dòng
│   ├── Contracts/
│   │   └── SendOtpEmailIntegrationEvent.cs            # record namespace Nexus.Contracts.Events (bản sao của User)
│   ├── Interfaces/
│   │   ├── INotificationDbContext.cs                  # 10 DbSet + SaveChangesAsync
│   │   └── IEmailSenderService.cs                     # HỢP ĐỒNG GỐC (2 method)
│   ├── Common/Interfaces/
│   │   └── IEmailSenderService.cs                     # interface rỗng, kế thừa Interfaces/IEmailSenderService
│   ├── Services/Email/
│   │   ├── IEmailNotificationService.cs               # facade use case (SendOtpEmailAsync, SendEmailAsync)
│   │   ├── EmailNotificationService.cs                # chỉ chuyển tiếp sang IEmailSenderService
│   │   └── EmailNotificationDtos.cs                   # SendOtpEmailDto, SendGeneralEmailDto
│   └── DependencyInjection.cs                         # AddApplicationServices()
│
├── Notification.Infrastructure/                       # 3. TẦNG INFRASTRUCTURE — 17 file .cs / 651 dòng
│   ├── Consumers/
│   │   └── SendOtpEmailConsumer.cs                    # 1 consumer duy nhất (đặt ở Infrastructure, không phải Application)
│   ├── Persistence/
│   │   ├── AppDbContext.cs                            # 10 DbSet, ApplyConfigurationsFromAssembly, MassTransit outbox
│   │   ├── Configurations/                            # 10 Fluent API configuration
│   │   ├── Migrations/                                # 20261024103253_InitialNotificationSchema (13 bảng)
│   │   └── Seeds/NotificationSeedData.cs              # 4 kênh, 2 template, 1 record
│   ├── Services/Email/
│   │   ├── IEmailService.cs                           # interface rỗng, kế thừa Application.Interfaces.IEmailSenderService
│   │   ├── SmtpOptions.cs                             # SectionName = "Smtp"
│   │   └── MailKitEmailSenderService.cs               # triển khai MailKit, đồng thời implement cả 3 interface
│   └── DependencyInjection.cs                         # DbContext, 3 đăng ký email, MassTransit + outbox
│
├── Notification.Api/                                  # 4. TẦNG API / HOST — 2 file .cs / 146 dòng
│   ├── Middleware/GlobalExceptionHandlerMiddleware.cs # ProblemDetails + RFC 9110 link
│   ├── Properties/launchSettings.json                 # 1 profile "http" → 8007 (iisExpress 41298 là rác template)
│   └── Program.cs                                     # DI, Swagger, migrate on startup, MapGet("/health")
│       # KHÔNG có thư mục Contracts/ và KHÔNG có file *.proto
│
└── Notification.Tests/                                # 5. TẦNG KIỂM THỬ — 1 file / 77 dòng
    └── EmailSenderTests.cs                            # 2 [Fact]: simulation mode + consumer gọi sender
```

---

## 2. NGUYÊN TẮC PHÂN CHIA TRÁCH NHIỆM

1. **`Notification.Domain`**: Entity POCO + enum + exception. Không import EF Core (đã đúng). Lưu ý format: file entity đặt `using` **trước** `namespace` (`Domain/Entities/NotificationRecord.cs:1-4`), ngược 7 repo còn lại.
2. **`Notification.Application`**: chỉ có `INotificationDbContext`, 3 biến thể interface email và facade `EmailNotificationService`. **Hiện chưa có use case thông báo nào** (list/mark-read/preference) — đó là đặc tả ở `specs/02`.
3. **`Notification.Infrastructure`**: `MailKitEmailSenderService` + `SendOtpEmailConsumer`. Consumer là nơi duy nhất nối event với email; **consumer không inject `INotificationDbContext`** nên không ghi `NotificationRecord`/`NotificationDelivery`.
4. **`Notification.Api`**: host mỏng, chỉ `/health`, Swagger ở Development và middleware lỗi. **Không có controller, không `MapGrpcService`, không authentication/authorization.**

### 2.1. Bảng trạng thái tính năng (nguồn: code, 2026-10-24)

| Tính năng | Mô tả kỳ vọng | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| Health check | `GET /health` trả JSON tĩnh | ✅ CÓ | `Notification.Api/Program.cs:49` |
| gRPC Server `NotificationApi` | 4 rpc: `ListNotifications`, `MarkNotificationRead`, `GetPreferences`, `UpdatePreferences` | ❌ CHƯA CÓ (0/4) | Không có `*.proto`; `Notification.Api/Notification.Api.csproj:15-18` không có `Grpc.AspNetCore`; `Program.cs` không có `MapGrpcService`/`AddGrpc` |
| REST controller | Endpoint cho client/gateway | ❌ CHƯA CÓ | 0 file `*Controller.cs`; chỉ `Program.cs:49` |
| Consume `SendOtpEmailIntegrationEvent` | Nhận event từ User rồi gửi email OTP | ✅ CÓ | `Notification.Infrastructure/Consumers/SendOtpEmailConsumer.cs:9` (khai báo), `:22-45` (xử lý) |
| Publish event | Phát event thông báo cho service khác | ❌ CHƯA CÓ | Không có `IPublishEndpoint`/`Publish(` trong repo |
| Ghi lịch sử thông báo | Lưu `NotificationRecord` + `NotificationDelivery` khi gửi | ❌ CHƯA CÓ | `SendOtpEmailConsumer.cs:14-20` chỉ inject `IEmailSenderService` + `ILogger`; `INotificationDbContext` đăng ký ở `DependencyInjection.cs:22` nhưng 0 inject |
| Gửi email SMTP thật | Gửi qua `smtp.gmail.com` | ⚠️ MỘT PHẦN | Code MailKit đầy đủ (`MailKitEmailSenderService.cs:43-62`) nhưng `appsettings.json:24-25` để `Username`/`Password` rỗng ⇒ nhánh `:34-41` log `[EMAIL SIMULATION]` và `return true` |
| Kiểm tra preference trước khi gửi | Không gửi nếu user tắt kênh | ❌ CHƯA CÓ | 0 dòng code đọc `NotificationPreferences` ngoài `INotificationDbContext.cs:12` |
| Retry / DLQ cho consumer | `UseMessageRetry`, `UseDelayedRedelivery` | ❌ CHƯA CÓ | `Infrastructure/DependencyInjection.cs:43-58` chỉ `cfg.Host(...)` + `cfg.ConfigureEndpoints(context)` |
| EF Outbox/Inbox | Bảng `InboxState`, `OutboxMessage`, `OutboxState` | ⚠️ MỘT PHẦN | `DependencyInjection.cs:37-41` (`AddEntityFrameworkOutbox<AppDbContext>` + `UsePostgres` + `UseBusOutbox`); bảng do migration tạo (`Migrations/20261024103253_InitialNotificationSchema.cs:107,153,225`); chưa có test/không có publisher nên chưa dùng thực tế |
| 10 bảng nghiệp vụ | 10 entity ↔ 10 DbSet ↔ 10 configuration | ✅ CÓ | `Persistence/AppDbContext.cs:14-23`; `Persistence/Configurations/` (10 file); migration `:18-335` |
| Migration + seed | Tạo schema và dữ liệu mẫu | ✅ CÓ | `Migrations/20261024103253_InitialNotificationSchema.cs`; `Persistence/Seeds/NotificationSeedData.cs:15-116` |
| OpenTelemetry | Trace/metric cho email | ❌ CHƯA CÓ | 0 package `OpenTelemetry*` trong 5 `.csproj`; `appsettings.json:27-29` có section `Otel` nhưng không ai đọc (config chết) |

> Ghi chú: **5/10 entity chưa từng được ghi** ở runtime: `EventLog`, `AuditLog`, `BrokerHealthCheck`, `DeviceToken`, `NotificationLog`. `NotificationRecord` chỉ có dữ liệu do seed (`NotificationSeedData.cs:98-116`), không do code nghiệp vụ ghi.

---

## 3. QUY TRÌNH THÊM TÍNH NĂNG MỚI (DÀNH CHO DEV & AI AGENT)

1. **Bước 1**: Tạo Entity & Enum trong `Notification.Domain/Entities/` + `Enums/`.
2. **Bước 2**: Tạo Configuration trong `Notification.Infrastructure/Persistence/Configurations/`, khai báo `DbSet<T>` ở **cả** `INotificationDbContext` (`Application/Interfaces/INotificationDbContext.cs:10-19`) **và** `AppDbContext` (`Infrastructure/Persistence/AppDbContext.cs:14-23`).
3. **Bước 3**: Tạo thư mục tính năng trong `Notification.Application/Services/{Feature}/` gồm DTO, interface và implementation.
4. **Bước 4**: Đăng ký service vào `Notification.Application/DependencyInjection.cs:10` (hiện chỉ có `IEmailNotificationService`).
5. **Bước 5**: Thêm consumer vào `Notification.Infrastructure/Consumers/` và đăng ký bằng `x.AddConsumer<T>()` tại `Infrastructure/DependencyInjection.cs:33`.
6. **Bước 6 (chưa từng làm trong repo này)**: Muốn có gRPC phải thêm **cả ba** thứ: file `.proto` trong repo, package `Grpc.AspNetCore` vào `Notification.Api.csproj`, và `app.MapGrpcService<T>()` trong `Program.cs`. Hiện cả ba đều **không có** — đừng giả định là đã có.
7. **Bước 7**: Nếu consumer cần ghi DB, inject `INotificationDbContext` (đã đăng ký sẵn ở `Infrastructure/DependencyInjection.cs:22`).

---

## 4. QUY CHUẨN THÔNG BÁO LỖI (MANDATORY ERROR RULES)

1. **100% English**: Tuyệt đối không dùng tiếng Việt trong chuỗi lỗi trả client.
2. **Sentence case**: Viết hoa duy nhất chữ cái đầu (`"Notification record not found."`).
3. **Error Code SCREAMING_SNAKE_CASE**: `NOTIFICATION_NOT_FOUND`, `INVALID_EMAIL`, `TEMPLATE_INACTIVE`.

**Hiện trạng middleware** (`Notification.Api/Middleware/GlobalExceptionHandlerMiddleware.cs`):

| Hành vi | Trạng thái | Bằng chứng (file:line) |
|:---|:---:|:---|
| Map exception → status + `errorCode` | ✅ CÓ | `:36-63` (`EntityNotFoundException`→404, `BusinessRuleValidationException`/`DomainException`/`ArgumentException`→400, còn lại→500) |
| Trả `application/problem+json` kèm `traceId` | ✅ CÓ | `:74-87` |
| Link RFC 9110 đúng section | ⚠️ SAI | `:79` dùng `15.5.1` cho **mọi** 4xx ⇒ 404 đang trỏ `15.5.1` (400 Bad Request) thay vì `15.5.5` (404 Not Found) |
| Log theo mức 4xx/5xx | ✅ CÓ | `:65-72` |

---

## 5. CÁCH CHẠY VÀ KIỂM THỬ ĐỘC LẬP SERVICE

```bash
# 0. Yêu cầu: .NET 8 SDK. Repo KHÔNG kèm dotnet-ef; cài global tool nếu cần migration:
dotnet tool install --global dotnet-ef --version 8.0.10

# 1. Bật database của Notification Service (Port 5439):
cd src/Services/Notification
docker compose up -d notification-db

# 2. Tạo & cập nhật migration (project đã có sẵn 1 migration Initial):
dotnet ef migrations add <TenMigration> --project Notification.Infrastructure --startup-project Notification.Api
dotnet ef database update --project Notification.Infrastructure --startup-project Notification.Api

# 3. Chạy service (http://localhost:8007, chỉ có /health):
dotnet run --project Notification.Api
curl http://localhost:8007/health

# 4. Build & test:
dotnet build Notification.sln -c Release
dotnet test Notification.Tests/Notification.Tests.csproj
```

> **Không có `./.tools/dotnet-ef`** trong repo — mọi hướng dẫn cũ trỏ vào đường dẫn này là sai. Dùng `dotnet ef` từ global tool.
> **Không có `Contracts/shop.proto`** trong `Notification.Api/` — hợp đồng gRPC chỉ tồn tại dưới dạng đặc tả ở `specs/02_API_GRPC_CONTRACTS.md`.

### 5.1. Công cụ/artifact đang lệch (đã kiểm chứng)

| Artifact | Hiện trạng | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `Notification.Api/Notification.Api.http` | Trỏ `http://localhost:5178` và gọi `/weatherforecast/` (endpoint không tồn tại) | ❌ SAI | `Notification.Api/Notification.Api.http:1,3` |
| `Properties/launchSettings.json` | Profile `http` = `8007` ✅; `iisSettings.applicationUrl` = `41298` là rác template, không dùng khi `dotnet run` | ⚠️ MỘT PHẦN | `:17` (8007), `:7` (41298) |
| `Dockerfile` | `EXPOSE 8007`, `ASPNETCORE_URLS=http://+:8007` | ✅ CÓ | `Dockerfile:3-4` |
| `docker-compose.yml` | Map `8007:8007` và `5439:5432` | ✅ CÓ | `:37`, `:19` |
| `nuget.config` | Ghim `globalPackagesFolder=/tmp/nuget_packages` (7 repo khác không có file này) | ⚠️ KHÁC BIỆT | `nuget.config:4` |
| CI | Chỉ build/push Docker image, **không** chạy `dotnet build`/`dotnet test` | ⚠️ MỘT PHẦN | `.github/workflows/ci-cd.yml:38-47` |
| `NotificationDbContext` | Lớp bọc trùng `AppDbContext`, không được đăng ký DI (chỉ `AppDbContext` được đăng ký) | ❌ CODE CHẾT | `Persistence/AppDbContext.cs:38-43`; `DependencyInjection.cs:19` |
| Test | 2 `[Fact]`, chỉ phủ email sender + consumer happy path | ⚠️ MỘT PHẦN | `Notification.Tests/EmailSenderTests.cs:18-38`, `:40-68` |
