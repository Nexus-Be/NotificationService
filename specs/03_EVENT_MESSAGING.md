# 03 — QUY CHUẨN GIAO TIẾP VÀ BÃI BỎ MESSAGE BROKER (NOTIFICATION SERVICE)

> **Trạng thái triển khai (2026-10-30):** 🛑 ĐÃ BÃI BỎ HOÀN TOÀN RABBITMQ & MASSTRANSIT — Toàn bộ cơ chế Consumer (`SendOtpEmailConsumer`), Outbox/Inbox tables đã được loại bỏ. Toàn bộ yêu cầu gửi email và tạo thông báo được phục vụ trực tiếp qua các RESTful API đồng bộ (`EmailController.cs`, `NotificationsController.cs`).
> **Nguồn sự thật:** code (`Notification.Api/Controllers/EmailController.cs`, `Notification.Api/Controllers/NotificationsController.cs`).
> **Cập nhật lần cuối:** 2026-10-30.

---

## 1. NGUYÊN TẮC BÃI BỎ MESSAGE BROKER

1. **Gỡ bỏ hoàn toàn MassTransit**:
   - Toàn bộ package `MassTransit`, `MassTransit.RabbitMQ`, `MassTransit.EntityFrameworkCore` đã được loại bỏ khỏi NotificationService.
   - Thư mục `Consumers/` và lớp `SendOtpEmailConsumer` không còn tồn tại.

2. **Tiếp Nhận Gửi Email & Thông Báo Qua REST API**:
   - Các dịch vụ liên quan (như `UserService` khi cần gửi OTP email) gọi trực tiếp endpoint REST đồng bộ:
     `POST /api/v1/notifications/email/otp`
   - Phản hồi HTTP `200 OK` hoặc `400 BadRequest` được trả về tức thì, bảo đảm minh bạch trạng thái xử lý mà không cần message broker trung gian.
