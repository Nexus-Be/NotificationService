# NOTIFICATION SERVICE SPEC KIT — CHỈ DẪN KỸ THUẬT CHO AI AGENT & DEVELOPER

> **Trạng thái triển khai (2026-10-27):** ✅ ĐÃ HOÀN TẤT & ĐỘC LẬP HOẠT ĐỘNG — Đầy đủ **9 endpoint RESTful** (2 controllers `NotificationsController` và `EmailController`), 7 test cases PASS 100%. Giao tiếp đồng bộ chuẩn **RESTful HTTP/JSON (CẤM gRPC)**. Lắng nghe sự kiện qua RabbitMQ MassTransit (`SendOtpEmailConsumer`).
> **Nguồn sự thật:** code (`Notification.Api/Controllers/`, `Notification.Tests/`, `Notification.Infrastructure/`).
> **Cập nhật lần cuối:** 2026-10-27.

Tài liệu này là **Bộ Chỉ Dẫn & Đặc Tả Kỹ Thuật (Spec Kit)** cho repository **NotificationService** thuộc hệ sinh thái Microservices **Nexus**. Giúp lập trình viên và các AI Coding Agent (Cursor, Claude, Copilot, DeepSeek...) nắm bắt bối cảnh quản lý thông báo, cấu hình tùy chọn thông báo người dùng và **loại bỏ hoàn toàn nguy cơ ảo giác (zero hallucination)**.

---

## 1. MỤC LỤC BỘ ĐẶC TẢ NOTIFICATION SERVICE (SPECS/)

| File Đặc Tả | Trọng Tâm Nội Dung | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| [00_AGENT_MISSION_GUARDRAILS.md](00_AGENT_MISSION_GUARDRAILS.md) | Bounded Context, Port `8007`, DB `notification_db` (`5439`), các điều cấm kỵ cốt lõi (CẤM gRPC). | ✅ HOÀN TẤT | Port `8007`, DB `5439`, 9 REST endpoints |
| [01_CLEAN_ARCHITECTURE_LAYERS.md](01_CLEAN_ARCHITECTURE_LAYERS.md) | Cấu trúc 4 tầng Clean Architecture (`.Api`, `.Application`, `.Domain`, `.Infrastructure`), luồng phụ thuộc. | ✅ HOÀN TẤT | 5 project dựng đúng tầng (`Notification.sln`) |
| [02_API_REST_CONTRACTS.md](02_API_REST_CONTRACTS.md) | Hợp đồng RESTful: Devices, In-app notifications, Preferences, Send Email & OTP. | ✅ HOÀN TẤT | `NotificationsController.cs`, `EmailController.cs` |
| [03_EVENT_MESSAGING.md](03_EVENT_MESSAGING.md) | Subscriber trung tâm nhận sự kiện từ toàn hệ thống qua RabbitMQ. | ✅ HOÀN TẤT | `Infrastructure/Consumers/SendOtpEmailConsumer.cs:9` |
| [04_DATA_PERSISTENCE.md](04_DATA_PERSISTENCE.md) | Mô hình thực thể, DbContext EF Core 8 (No Repository Pattern). | ✅ HOÀN TẤT | `AppDbContext.cs:14-23`, 13 bảng trong migration |
| [05_DEV_VERIFICATION_CHECKLIST.md](05_DEV_VERIFICATION_CHECKLIST.md) | Lệnh biên dịch, kiểm thử tự động, checklist trước khi commit. | ✅ HOÀN TẤT | 7 test cases PASS (`Notification.Tests/`) |

---

## 2. THÔNG SỐ VẬN HÀNH CHÍNH

| Thông số | Giá trị thật | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| Ngôn ngữ & Runtime | .NET 8 (`net8.0`), C# 12 | ✅ CÓ | `Notification.Api/Notification.Api.csproj:4`; 5/5 project cùng TFM |
| Cổng service | `8007` — HTTP RESTful (`http://localhost:8007`) | ✅ CÓ | `Dockerfile:3-4`; `docker-compose.yml:37`; `Properties/launchSettings.json:17` |
| Cổng HTTP REST | Đầy đủ 9 endpoints nghiệp vụ (`/api/v1/notifications/*`, `/api/v1/notifications/email/*`) | ✅ CÓ | `NotificationsController.cs`, `EmailController.cs` |
| Cơ sở dữ liệu | PostgreSQL `notification_db`, port host `5439` | ✅ CÓ | `appsettings.json:10`; `docker-compose.yml:19` |
| Message Broker | RabbitMQ `5672`, vhost `/` | ✅ CÓ | `appsettings.json:12-16`; `Infrastructure/DependencyInjection.cs:43-58` |
| SMTP Email Provider | MailKit SMTP & Simulation Mode an toàn cho dev | ✅ CÓ | `Services/Email/MailKitEmailSenderService.cs` |
| Giao thức đồng bộ | RESTful HTTP/JSON (**CẤM TUYỆT ĐỐI gRPC**) | ✅ CÓ | Bãi bỏ hoàn toàn Protobuf và gRPC Server |
