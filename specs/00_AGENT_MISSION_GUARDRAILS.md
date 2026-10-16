# 00 — NHIỆM VỤ CỐT LÕI & RÀO CHẮN CHỐNG ẢO GIÁC (NOTIFICATION SERVICE)

---

## 1. NHIỆM VỤ & RANH GIỚI MIỀN (BOUNDED CONTEXT)

**NotificationService** chịu trách nhiệm độc quyền về việc gửi và lưu trữ thông báo cho người dùng:
- Lưu trữ lịch sử thông báo in-app (`Notification`): Tiêu đề, nội dung tóm tắt, trạng thái đã đọc (`IsRead`), đường dẫn hành động (`ActionUrl`).
- Đánh dấu thông báo đã đọc (`MarkNotificationRead`).
- Quản lý tùy chọn nhận thông báo của người dùng (`NotificationPreferences`):
  - `EmailEnabled`: Cho phép nhận email.
  - `PushEnabled`: Cho phép nhận push notification trên thiết bị di động / trình duyệt.
  - `OrderUpdates`: Nhận thông báo tiến độ đơn hàng.
  - `AuctionAlerts`: Nhận thông báo khi bị outbid hoặc khi phiên đấu giá sắp hết hạn.
- Đóng vai trò là **Subscriber trung tâm** lắng nghe các sự kiện từ toàn bộ hệ thống để phát sinh thông báo tương ứng.
- Cung cấp dịch vụ gRPC Server tại cổng `8007` cho ApiGateway.

---

## 2. NHỮNG ĐIỀU CẤM KỴ ĐỐI VỚI AI AGENTS (ANTI-HALLUCINATION GUARDRAILS)

1. **TUYỆT ĐỐI KHÔNG DÙNG REPOSITORY PATTERN BỌC NGOÀI EF CORE**:
   - Sử dụng trực tiếp `NotificationDbContext` và `DbSet<T>` trong tầng Application.
   - Không được tạo `INotificationRepository`, `IPreferenceRepository`, `UnitOfWork`.

2. **KHÔNG GỬI THÔNG BÁO NẾU USER ĐÃ TẮT PREFERENCES TƯƠNG ỨNG**:
   - Trước khi gửi email hoặc push, luôn kiểm tra `NotificationPreferences` của user. Nếu user đã tắt `OrderUpdates` $\rightarrow$ Không gửi email về đơn hàng.

3. **TUYỆT ĐỐI KHÔNG THAY ĐỔI CỔNG 8007 VÀ DATABASE 5438**:
   - Service gRPC chạy tại `http://localhost:8007`.
   - Chuỗi kết nối DB trỏ tới `localhost:5438/notification_db`.
