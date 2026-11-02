# 00 — NHIỆM VỤ CỐT LÕI & RÀO CHẮN CHỐNG ẢO GIÁC (NOTIFICATION SERVICE)

> **Trạng thái triển khai (2026-11-02):** ✅ ĐÃ HOÀN TẤT & ĐỘC LẬP HOẠT ĐỘNG — Đầy đủ **9 endpoint RESTful công khai** (2 controllers `NotificationsController` và `EmailController`), xử lý gửi OTP email và thông báo trực tiếp qua REST. Đã bãi bỏ hoàn toàn gRPC và RabbitMQ MassTransit. Đã pass 100% test độc lập (`Notification.Tests`).
> **Nguồn sự thật:** code (`Notification.Api/Controllers/Public/NotificationsController.cs`, `Notification.Api/Controllers/Public/EmailController.cs`, `Notification.Infrastructure/Services/MailKitEmailSenderService.cs`).
> **Cập nhật lần cuối:** 2026-11-02.

---

## 1. NHIỆM VỤ & RANH GIỚI MIỀN (BOUNDED CONTEXT)

**NotificationService** chịu trách nhiệm độc quyền về việc gửi email và lưu trữ thông báo cho người dùng trên sàn Nexus:

| # | Nhiệm vụ | Trạng thái | Bằng chứng (file:line) |
|:--|:---|:---:|:---|
| 1 | Lưu trữ lịch sử thông báo in-app (`NotificationRecord`): `UserId`, `Title`, `Body`, `Payload` (jsonb), `IsRead`, `ReadAt`, `Status` | ✅ HOẠT ĐỘNG | `Notification.Api/Controllers/NotificationsController.cs:64-75`, `Domain/Entities/NotificationRecord.cs:6-23` |
| 2 | Đánh dấu thông báo đã đọc (`MarkNotificationRead`) | ✅ HOẠT ĐỘNG | `Notification.Api/Controllers/NotificationsController.cs:77-88` |
| 3 | Quản lý tùy chọn nhận thông báo (`NotificationPreferences`) | ✅ HOẠT ĐỘNG | `Notification.Api/Controllers/NotificationsController.cs:46-62` |
| 4 | Quản lý thiết bị nhận push (`RegisterDeviceRequest` / `Unregister`) | ✅ HOẠT ĐỘNG | `Notification.Api/Controllers/NotificationsController.cs:25-44` |
| 5 | Dịch vụ gửi email OTP và thông báo trực tiếp qua RESTful API | ✅ HOẠT ĐỘNG | `Notification.Api/Controllers/Public/EmailController.cs:20-55`, `MailKitEmailSenderService.cs` |
| 6 | Giao tiếp liên dịch vụ 100% qua RESTful HTTP (Bãi bỏ RabbitMQ / MassTransit) | ✅ HOẠT ĐỘNG | `Notification.Api/Program.cs`, `specs/03_EVENT_MESSAGING.md` |
| 7 | Cung cấp giao diện RESTful HTTP/JSON tại cổng `8007` (CẤM gRPC) | ✅ HOẠT ĐỘNG | `Notification.Api/Program.cs`, `Dockerfile:3-4` |

---

## 2. NHỮNG ĐIỀU CẤM KỴ ĐỐI VỚI AI AGENTS (ANTI-HALLUCINATION GUARDRAILS)

1. **TUYỆT ĐỐI KHÔNG DÙNG REPOSITORY PATTERN BỌC NGOÀI EF CORE**:
   - Sử dụng trực tiếp `INotificationDbContext` / `AppDbContext` và `DbSet<T>` trong tầng Application và Controllers.
   - Không được tạo `INotificationRepository`, `IPreferenceRepository`, `UnitOfWork`.

2. **CẤM SỬ DỤNG gRPC CHO GIAO TIẾP ĐỒNG BỘ — 100% SỬ DỤNG RESTFUL HTTP/JSON**:
   - Mọi giao tiếp đồng bộ từ ApiGateway hoặc service khác xuống NotificationService bắt buộc phải qua các endpoint RESTful HTTP/JSON tại port `8007`.
   - Bãi bỏ hoàn toàn các tệp Protobuf `shop.proto` và package `Grpc.AspNetCore`.

3. **TUYỆT ĐỐI KHÔNG THAY ĐỔI CỔNG 8007 VÀ DATABASE 5439**:
   - Service chạy tại `http://localhost:8007` — HTTP RESTful (`Dockerfile:3-4`, `Properties/launchSettings.json:17`, `docker-compose.yml:37`).
   - Chuỗi kết nối DB trỏ `localhost:5439/notification_db` (`appsettings.json:10`, `docker-compose.yml:19`).
