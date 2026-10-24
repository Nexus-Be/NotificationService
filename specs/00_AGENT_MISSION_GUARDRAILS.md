# 00 — NHIỆM VỤ CỐT LÕI & RÀO CHẮN CHỐNG ẢO GIÁC (NOTIFICATION SERVICE)

> **Trạng thái triển khai (2026-10-24):** ❌ CHƯA CÓ — **0/4 rpc**, **0 controller**, **0 dòng code** đọc `NotificationPreferences` và chỉ **1/9 sự kiện** có consumer; phần chạy được chỉ là nhận `SendOtpEmailIntegrationEvent` rồi gọi email sender.
> **Nguồn sự thật:** code (`Notification.Api/Program.cs:49`, `Notification.Infrastructure/Consumers/SendOtpEmailConsumer.cs:9`, `Notification.Infrastructure/Persistence/AppDbContext.cs:14-23`).
> **Cập nhật lần cuối:** 2026-10-24.

---

## 1. NHIỆM VỤ & RANH GIỚI MIỀN (BOUNDED CONTEXT)

**NotificationService** được đặc tả để chịu trách nhiệm độc quyền về việc gửi và lưu trữ thông báo cho người dùng. Bảng dưới tách rõ **đặc tả** và **phần đã có trong code**.

| # | Nhiệm vụ (đặc tả) | Trạng thái | Bằng chứng (file:line) |
|:--|:---|:---:|:---|
| 1 | Lưu trữ lịch sử thông báo in-app (`NotificationRecord`): `UserId`, `Title`, `Body`, `Payload` (jsonb), `IsRead`, `ReadAt`, `Status` | ❌ CHƯA CÓ | Entity có (`Domain/Entities/NotificationRecord.cs:6-23`) nhưng **không có code ghi**: `SendOtpEmailConsumer.cs:14-20` không inject `INotificationDbContext`; chỉ seed 1 bản ghi (`Persistence/Seeds/NotificationSeedData.cs:98-116`) |
| 2 | Đánh dấu thông báo đã đọc (`MarkNotificationRead`) | ❌ CHƯA CÓ | Không có method/rpc nào; `Notification.Api/Program.cs` chỉ có `/health` |
| 3 | Quản lý tùy chọn nhận thông báo (`NotificationPreferences`) | ❌ CHƯA CÓ | Entity `NotificationPreference` chỉ có `UserId`, `NotificationType`, `ChannelCode`, `IsDisabled` (`Domain/Entities/NotificationPreference.cs:6-15`); **không có** `EmailEnabled`/`PushEnabled`/`OrderUpdates`/`AuctionAlerts`; 0 use case đọc/ghi |
| 4 | Subscriber trung tâm lắng nghe sự kiện toàn hệ thống | ⚠️ MỘT PHẦN (1/9) | Chỉ `SendOtpEmailConsumer` (`Infrastructure/Consumers/SendOtpEmailConsumer.cs:9`); 8 event còn lại là đặc tả ở `03_EVENT_MESSAGING.md` §1 |
| 5 | Cung cấp gRPC Server tại cổng `8007` cho ApiGateway | ❌ CHƯA CÓ | Không có `*.proto`, không `Grpc.AspNetCore` (`Notification.Api/Notification.Api.csproj:15-18`), không `MapGrpcService`; port `8007` chỉ phục vụ HTTP/1.1 (`Dockerfile:3-4`) |
| 6 | Kiểm tra preference trước khi gửi (chống spam) | ❌ CHƯA CÓ | 0 dòng code đọc `NotificationPreferences` ngoài khai báo DbSet (`Application/Interfaces/INotificationDbContext.cs:12`) |

> **Không có trường `ActionUrl`** trong bất kỳ entity nào. Deep link (nếu cần) phải nằm trong `Payload` kiểu `jsonb` (`NotificationRecord.cs:15`, cấu hình tại `Persistence/Configurations/NotificationRecordConfiguration.cs:16`). Tài liệu cũ nhắc `ActionUrl` là **không tồn tại trong code**.

---

## 2. NHỮNG ĐIỀU CẤM KỴ ĐỐI VỚI AI AGENTS (ANTI-HALLUCINATION GUARDRAILS)

1. **TUYỆT ĐỐI KHÔNG DÙNG REPOSITORY PATTERN BỌC NGOÀI EF CORE**:
   - Sử dụng trực tiếp `INotificationDbContext` / `AppDbContext` và `DbSet<T>` trong tầng Application.
   - Không được tạo `INotificationRepository`, `IPreferenceRepository`, `UnitOfWork`.
   - **Hiện trạng:** ✅ tuân thủ — chỉ có `INotificationDbContext` (`Application/Interfaces/INotificationDbContext.cs:8`) và `AppDbContext` (`Infrastructure/Persistence/AppDbContext.cs:8`), không có repository nào.

2. **KHÔNG GỬI THÔNG BÁO NẾU USER ĐÃ TẮT PREFERENCES TƯƠNG ỨNG**:
   - Trước khi gửi email hoặc push phải kiểm tra `NotificationPreferences` của user.
   - **Hiện trạng:** ❌ **chưa có một dòng code nào thực hiện việc này.** `SendOtpEmailConsumer.cs:22-45` gọi thẳng `_emailSender.SendOtpRegistrationEmailAsync(...)`. Đây là **đặc tả mục tiêu**, không phải hành vi hiện tại.

3. **TUYỆT ĐỐI KHÔNG THAY ĐỔI CỔNG 8007 VÀ DATABASE 5439**:
   - Service chạy tại `http://localhost:8007` — **HTTP/1.1**, không phải gRPC (`Dockerfile:3-4`, `Properties/launchSettings.json:17`, `docker-compose.yml:37`).
   - Chuỗi kết nối DB trỏ `localhost:5439/notification_db` (`appsettings.json:10`, `docker-compose.yml:19`). Mật khẩu mặc định đang là secret thật trong repo — khi sửa tài liệu **không chép thêm** giá trị này vào chỗ mới.

4. **KHÔNG MÔ TẢ NĂNG LỰC CHƯA CÓ**: mọi tuyên bố về rpc/endpoint/event/bảng phải kèm `file:line`. Nếu chỉ là đặc tả, ghi rõ "đặc tả mục tiêu, chưa triển khai" + nhãn ❌.
