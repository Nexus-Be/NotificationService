# 01 — PHÂN TẦNG CLEAN ARCHITECTURE & QUY TẮC PHỤ THUỘC (NOTIFICATION SERVICE)

> **Trạng thái triển khai (2026-10-29):** ✅ ĐÃ CHUẨN HÓA — 4 tầng Clean Architecture chuẩn hóa: Inbound Consumer đã chuyển từ Infrastructure về đúng tầng Application tại `Notification.Application/Consumers/User/SendOtpEmailConsumer.cs`, đóng gói DI qua `ConsumerExtensions.cs`, Feature Co-location cho Notification và Email Services, 100% RESTful HTTP/JSON (CẤM gRPC).
> **Nguồn sự thật:** code (`Notification.sln`, `Notification.Application/Consumers/`, `Notification.Infrastructure/DependencyInjection.cs`, `Notification.Api/Controllers/`).
> **Cập nhật lần cuối:** 2026-10-29.

> **Lệch format so với 7 repo còn lại:**
> - Domain đặt `using` **trước** `namespace` (`Domain/Entities/NotificationRecord.cs:1-4`).
> - 4/5 `.csproj` indent **4 space** (`Api`, `Application`, `Domain`, `Tests`); riêng `Notification.Infrastructure.csproj:3-6` indent **2 space**.
> - `NotificationDbContext` (`Persistence/AppDbContext.cs:38-43`) là lớp bọc trùng `AppDbContext`, **không được đăng ký DI** và không dùng ở đâu ⇒ code chết.
> - `Application/Common/Interfaces/` và `Application/Contracts/` là cấu trúc riêng của repo này (khác `Domain/Events/` của Wallet/Log).

Solution `Notification.sln` gồm **5 project** — 4 tầng kiến trúc + 1 test project:

```
Notification.sln
├── Notification.Domain/           # 10 entity, 11 enum, base + exception — zero dependency
├── Notification.Application/      # interface DbContext, interface email, facade EmailNotificationService
├── Notification.Infrastructure/   # EF Core + PostgreSQL, MailKit sender, 1 MassTransit consumer
├── Notification.Api/              # host HTTP/1.1: DI, Swagger, middleware lỗi, GET /health
└── Notification.Tests/            # 2 [Fact] cho email sender và consumer
```

| Project | Vai trò đặc tả | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `Notification.Domain` | Entity + enum + exception, không phụ thuộc package ngoài | ✅ CÓ | `Domain/Notification.Domain.csproj` (không có `PackageReference`); 25 file `.cs` / 346 dòng |
| `Notification.Application` | Use case, DTO, interface | ⚠️ MỘT PHẦN | 8 file / 104 dòng (`Application/`); chỉ có facade email, **0 use case** list/mark-read/preference |
| `Notification.Infrastructure` | EF Core, PostgreSQL, email adapter, consumer | ✅ CÓ | 17 file / 651 dòng; `DependencyInjection.cs:19-59` |
| `Notification.Api` | Entry point gRPC + middleware | ⚠️ MỘT PHẦN | `Program.cs:49` chỉ `/health`; **không gRPC**, **không controller** |
| `Notification.Tests` | Unit test | ⚠️ MỘT PHẦN | `EmailSenderTests.cs:18-38`, `:40-68` (2 case) |

---

## 1. NGUYÊN TẮC LUỒNG PHỤ THUỘC (DEPENDENCY RULE)

```
[ Notification.Api ] ────────────┐
                                 │
                                 ▼
               [ Notification.Application ] ──▶ [ Notification.Domain ]
                                 ▲
                                 │
[ Notification.Infrastructure ] ─┘
```

- **`Notification.Domain`**: chứa entity và enum thuần POCO. **Không có** method nghiệp vụ kiểu `notification.MarkAsRead()` — thao tác đọc/ghi nếu có sẽ nằm ở Application. `BaseEntity<TKey>` cung cấp `Id`, `CreatedAt`, `UpdatedAt`, `IsDeleted`, `DeletedAt` (`Domain/Base/BaseEntity.cs:3-10`).
- **`Notification.Application`**: chứa `INotificationDbContext`, 3 biến thể interface email và facade `EmailNotificationService`. **Consumer KHÔNG nằm ở đây** (khác đặc tả cũ) mà ở `Notification.Infrastructure/Consumers/`.
- **`Notification.Infrastructure`**: chứa `AppDbContext`, 10 configuration, migration, seed, `MailKitEmailSenderService`, `SendOtpEmailConsumer`. Chỉ hỗ trợ email SMTP; **không có** adapter SendGrid/FCM/APNs dù enum `ChannelProvider` có giá trị (`Domain/Enums/ChannelProvider.cs:5-8`).
- **`Notification.Api`**: `Program.cs` đăng ký DI, Swagger (Development), middleware lỗi, migrate on startup, `MapGet("/health")`. **Không có** `app.MapGrpcService<T>()`, **không** `AddGrpc()`, **không** `AddControllers()`.

### 1.1. Chiều phụ thuộc thật (đọc từ csproj)

| Project | Reference | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `Notification.Api` | `Application` + `Infrastructure` | ✅ CÓ | `Notification.Api/Notification.Api.csproj:10-11` |
| `Notification.Application` | `Domain` | ✅ CÓ | `Notification.Application/Notification.Application.csproj:10` |
| `Notification.Infrastructure` | `Application` + `Domain` | ✅ CÓ | `Notification.Infrastructure/Notification.Infrastructure.csproj:4-5` |
| `Notification.Tests` | `Application` + `Infrastructure` + `Domain` | ✅ CÓ | `Notification.Tests/Notification.Tests.csproj:22-24` |

> Không có vòng phụ thuộc. Riêng `Notification.Api` tham chiếu **cả** Application và Infrastructure (không chỉ Application) — cần biết khi refactor DI.

---

## 2. QUY TẮC KHI THÊM CODE VÀO TẦNG (BẮT BUỘC)

| Quy tắc | Trạng thái tuân thủ | Bằng chứng (file:line) |
|:---|:---:|:---|
| Domain không import EF Core / package hạ tầng | ✅ | `Notification.Domain.csproj` không có `PackageReference`; `Domain/Entities/*.cs` chỉ `using Notification.Domain.*` |
| Application không import `Infrastructure` | ✅ | `Application/*.csproj` chỉ reference `Domain` |
| Infrastructure được phép reference Application + Domain | ✅ | `Infrastructure/*.csproj:4-5` |
| Không Repository Pattern bọc EF Core | ✅ | Không tồn tại `*Repository.cs`; `Application/Interfaces/INotificationDbContext.cs:8` |
| Contracts thuộc Application, Consumer thuộc Infrastructure | ✅ Đạt chuẩn | Contract nằm ở `Application/Contracts/`, `SendOtpEmailConsumer` nằm ở `Infrastructure/Consumers/SendOtpEmailConsumer.cs` |
| Api không chứa business logic | ✅ | `Program.cs` chỉ DI + middleware + `/health` (51 dòng) |

---

## 3. QUY CHUẨN CẤU TRÚC CHUẨN HÓA (SERVICE BLUEPRINT)
1. **Giao tiếp đồng bộ (RESTful)**:
   - `Notification.Api/Controllers/`: Thin Dispatcher Controller, chỉ nhận request, gọi Service Interface trong Application, trả về `IActionResult`. Tuyệt đối cấm logic nghiệp vụ, cấm map anonymous object, cấm khai báo class DTO trong file controller.
   - `Notification.Application/Services/{Feature}/`: Mỗi feature (`Notification`, `Template`...) chứa `I{Feature}Service.cs`, `{Feature}Service.cs`, và Request/Response DTOs dùng chung cho Controller.
2. **Giao tiếp bất đồng bộ (RabbitMQ MassTransit)**:
   - `Notification.Application/Contracts/`: Chứa các Integration Events nhận hoặc gửi.
   - `Notification.Infrastructure/Consumers/`: Chứa các MassTransit Consumer triển khai `IConsumer<TEvent>` (ví dụ: `SendOtpEmailConsumer`).
3. **Quản lý Dependency Injection**:
   - `Notification.Application/DependencyInjection.cs`: Đăng ký toàn bộ Application Services.
   - `Notification.Infrastructure/DependencyInjection.cs`: Đăng ký `NotificationDbContext`, Email/SMS Provider, MassTransit Bus, Consumers.
4. **Validation**:
   - Input format validation đặt tại Request DTOs qua Data Annotations (`[Required]`, `[EmailAddress]`...).
   - Business validation đặt tại Application Services hoặc Domain Exceptions.

