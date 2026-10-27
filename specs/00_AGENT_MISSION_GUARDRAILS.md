# 00 — NHIỆM VỤ CỐT LÕI & RÀO CHẮN CHỐNG ẢO GIÁC (NOTIFICATION SERVICE)

> **Trạng thái triển khai (2026-10-27):** ✅ ĐÃ HOÀN TẤT & ĐỘC LẬP HOẠT ĐỘNG — Đầy đủ **9 endpoint RESTful** (2 controllers `NotificationsController` và `EmailController`), xử lý gửi OTP email trực tiếp qua REST và nhận bất đồng bộ qua `SendOtpEmailIntegrationEvent`. Đã bãi bỏ hoàn toàn gRPC. Đã pass 100% test độc lập (`Notification.Tests`).
> **Nguồn sự thật:** code (`Notification.Api/Controllers/NotificationsController.cs`, `Notification.Api/Controllers/EmailController.cs`, `Notification.Infrastructure/Consumers/SendOtpEmailConsumer.cs`, `Notification.Infrastructure/Services/MailKitEmailSenderService.cs`).
> **Cập nhật lần cuối:** 2026-10-27.

---

## 1. NHIỆM VỤ & RANH GIỚI MIỀN (BOUNDED CONTEXT)

**NotificationService** chịu trách nhiệm độc quyền về việc gửi email và lưu trữ thông báo cho người dùng trên sàn Nexus:

| # | Nhiệm vụ | Trạng thái | Bằng chứng (file:line) |
|:--|:---|:---:|:---|
| 1 | Lưu trữ lịch sử thông báo in-app (`NotificationRecord`): `UserId`, `Title`, `Body`, `Payload` (jsonb), `IsRead`, `ReadAt`, `Status` | ✅ HOẠT ĐỘNG | `Notification.Api/Controllers/NotificationsController.cs:64-75`, `Domain/Entities/NotificationRecord.cs:6-23` |
| 2 | Đánh dấu thông báo đã đọc (`MarkNotificationRead`) | ✅ HOẠT ĐỘNG | `Notification.Api/Controllers/NotificationsController.cs:77-88` |
| 3 | Quản lý tùy chọn nhận thông báo (`NotificationPreferences`) | ✅ HOẠT ĐỘNG | `Notification.Api/Controllers/NotificationsController.cs:46-62` |
| 4 | Quản lý thiết bị nhận push (`RegisterDeviceRequest` / `Unregister`) | ✅ HOẠT ĐỘNG | `Notification.Api/Controllers/NotificationsController.cs:25-44` |
| 5 | Dịch vụ gửi email OTP và thông báo trực tiếp qua RESTful API | ✅ HOẠT ĐỘNG | `Notification.Api/Controllers/EmailController.cs:20-55`, `MailKitEmailSenderService.cs` |
| 6 | Subscriber nhận sự kiện bất đồng bộ qua RabbitMQ | ✅ HOẠT ĐỘNG | `Infrastructure/Consumers/SendOtpEmailConsumer.cs:9` (`SendOtpEmailIntegrationEvent`) |
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
