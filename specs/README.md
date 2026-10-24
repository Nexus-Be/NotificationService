# NOTIFICATION SERVICE SPEC KIT — CHỈ DẪN KỸ THUẬT CHO AI AGENT & DEVELOPER

> **Trạng thái triển khai (2026-10-24):** ⚠️ MỘT PHẦN — bộ spec mô tả đúng phần đã có (10 bảng, 1 consumer, email MailKit) nhưng **0/4 rpc**, **0 controller**, **1/9 event** và consumer **không ghi DB** vẫn chỉ là đặc tả mục tiêu.
> **Nguồn sự thật:** code (`Notification.Api/Program.cs:49`, `Notification.Infrastructure/Consumers/SendOtpEmailConsumer.cs:9`, `Notification.Infrastructure/Persistence/AppDbContext.cs:14-23`).
> **Cập nhật lần cuối:** 2026-10-24.

Tài liệu này là **Bộ Chỉ Dẫn & Đặc Tả Kỹ Thuật (Spec Kit)** cho repository **NotificationService** thuộc hệ sinh thái Microservices **Nexus**. Giúp lập trình viên và các AI Coding Agent (Cursor, Claude, Copilot, DeepSeek...) nắm bắt bối cảnh quản lý thông báo, cấu hình tùy chọn thông báo người dùng và **loại bỏ hoàn toàn nguy cơ ảo giác (zero hallucination)**.

---

## 1. MỤC LỤC BỘ ĐẶC TẢ NOTIFICATION SERVICE (SPECS/)

| File Đặc Tả | Trọng Tâm Nội Dung | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| [00_AGENT_MISSION_GUARDRAILS.md](00_AGENT_MISSION_GUARDRAILS.md) | Bounded Context, Port `8007`, DB `notification_db` (`5439`), các điều cấm kỵ cốt lõi. | ⚠️ MỘT PHẦN | Port ✅ (`Properties/launchSettings.json:17`); 3/5 nhiệm vụ miền chưa có code |
| [01_CLEAN_ARCHITECTURE_LAYERS.md](01_CLEAN_ARCHITECTURE_LAYERS.md) | Cấu trúc 4 tầng Clean Architecture (`.Api`, `.Application`, `.Domain`, `.Infrastructure`), luồng phụ thuộc. | ⚠️ MỘT PHẦN | 5 project dựng đúng tầng (`Notification.sln:4-13`); Application 104 dòng, consumer nằm ở Infrastructure |
| [02_API_GRPC_CONTRACTS.md](02_API_GRPC_CONTRACTS.md) | Hợp đồng gRPC `NotificationApi`, danh sách thông báo, đánh dấu đọc, preferences. | ❌ CHƯA CÓ (0/4 rpc) | Không `*.proto`; `Notification.Api.csproj:15-18` không `Grpc.AspNetCore`; `Program.cs:49` chỉ `/health` |
| [03_EVENT_MESSAGING.md](03_EVENT_MESSAGING.md) | Subscriber trung tâm nhận sự kiện từ toàn hệ thống qua RabbitMQ. | ⚠️ MỘT PHẦN (1/9) | `Infrastructure/Consumers/SendOtpEmailConsumer.cs:9`; các event còn lại chỉ là đặc tả |
| [04_DATA_PERSISTENCE.md](04_DATA_PERSISTENCE.md) | Mô hình thực thể, DbContext EF Core 8 (No Repository Pattern). | ⚠️ MỘT PHẦN | 10 DbSet (`AppDbContext.cs:14-23`), 13 bảng trong migration; 5 entity chưa từng ghi |
| [05_DEV_VERIFICATION_CHECKLIST.md](05_DEV_VERIFICATION_CHECKLIST.md) | Lệnh biên dịch, kiểm thử tự động, checklist trước khi commit. | ⚠️ MỘT PHẦN | 2 test (`Notification.Tests/EmailSenderTests.cs:18,40`); CI không chạy test |

---

## 2. THÔNG SỐ VẬN HÀNH CHÍNH

| Thông số | Giá trị thật | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| Ngôn ngữ & Runtime | .NET 8 (`net8.0`), C# 12 | ✅ CÓ | `Notification.Api/Notification.Api.csproj:4`; 5/5 project cùng TFM |
| Cổng service | `8007` — **HTTP/1.1 only**, không mở HTTP/2, không gRPC | ⚠️ MỘT PHẦN | `Dockerfile:3-4`; `docker-compose.yml:37`; `Properties/launchSettings.json:17` |
| Cổng HTTP REST riêng | Chỉ có `GET /health`; **không** có route `/api/v1/*` | ❌ CHƯA CÓ | `Notification.Api/Program.cs:49` (dòng `Map*` duy nhất) |
| Cơ sở dữ liệu | PostgreSQL `notification_db`, port host `5439` | ✅ CÓ | `appsettings.json:10`; `docker-compose.yml:19` |
| Mật khẩu DB mặc định | `MatkhaucuaTui123@` — **secret thật đang nằm trong repo** | ⚠️ RỦI RO | `appsettings.json:10`; `.env.example:8`; `docker-compose.yml:17`; `DependencyInjection.cs:17` |
| Message Broker | RabbitMQ `5672`, vhost `/` | ✅ CÓ | `appsettings.json:12-16`; `Infrastructure/DependencyInjection.cs:43-58` |
| SMTP | `smtp.gmail.com:587`, `StartTlsWhenAvailable` | ⚠️ MỘT PHẦN | `appsettings.json:18-26` (credential rỗng); `Services/Email/MailKitEmailSenderService.cs:54` |
| OpenTelemetry | Có section config, **không có package** ⇒ config chết | ❌ CHƯA CÓ | `appsettings.json:27-29`; 5 `.csproj` không có `OpenTelemetry*` |
| Hợp đồng gRPC | Không có file `.proto` nào trong repo | ❌ CHƯA CÓ | `find . -name "*.proto"` → rỗng |

> **Port bất biến**: `8007` (service) và `5439` (DB) khớp quy hoạch. Riêng `iisSettings.applicationUrl = 41298` trong `Properties/launchSettings.json:7` là rác template, không ảnh hưởng `dotnet run` (profile `http` được chọn).
