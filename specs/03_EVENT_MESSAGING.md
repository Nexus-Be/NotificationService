# 03 — SỰ KIỆN BẤT ĐỒNG BỘ QUA RABBITMQ (NOTIFICATION SERVICE EVENT MESSAGING)

> **Trạng thái triển khai (2026-10-24):** ⚠️ MỘT PHẦN — **1/9** sự kiện có consumer (`SendOtpEmailIntegrationEvent`); 8 sự kiện còn lại chỉ là đặc tả. Consumer **không ghi DB**, chỉ gọi email sender + log.
> **Nguồn sự thật:** code (`Notification.Infrastructure/Consumers/SendOtpEmailConsumer.cs:9-45`, `Notification.Infrastructure/DependencyInjection.cs:31-59`).
> **Cập nhật lần cuối:** 2026-10-24.

NotificationService được đặc tả là **Subscriber trung tâm** nhận sự kiện từ các service khác trong hệ sinh thái. Hiện chỉ có **một** consumer thật.

---

## 1. SỰ KIỆN LẮNG NGHE & HÀNH ĐỘNG TƯƠNG ỨNG

| Sự Kiện Nhận Được | Service Phát Hành | Hành Động Tạo Thông Báo (đặc tả) | Trạng thái | Bằng chứng (file:line) |
|:---|:---:|:---|:---:|:---|
| `SendOtpEmailIntegrationEvent` | `UserService` | Gửi email chứa mã OTP đăng ký (thay cho event "welcome" ban đầu). | ✅ CÓ | `Infrastructure/Consumers/SendOtpEmailConsumer.cs:9,22-45` |
| `UserRegisteredEvent` | `UserService` | Tạo thông báo chào mừng thành viên mới, gửi email kích hoạt. | ❌ CHƯA CÓ | 0 consumer; `DependencyInjection.cs:33` chỉ `AddConsumer<SendOtpEmailConsumer>` |
| `SellerApprovedEvent` | `UserService` | Tạo thông báo chúc mừng tài khoản đã được nâng cấp thành Seller. | ❌ CHƯA CÓ | Như trên |
| `OrderPaidEvent` | `CommerceService` | Tạo thông báo xác nhận thanh toán thành công, đơn hàng đang được chuẩn bị. | ❌ CHƯA CÓ | Như trên |
| `OrderCancelledEvent` | `CommerceService` | Tạo thông báo đơn hàng đã bị hủy kèm lý do chi tiết. | ❌ CHƯA CÓ | Như trên |
| `BidPlacedEvent` | `AuctionService` | Tạo thông báo cho người giữ giá trước đó: "Bạn vừa bị đặt giá vượt qua (outbid)!". | ❌ CHƯA CÓ | Như trên |
| `AuctionEndedEvent` | `AuctionService` | Tạo thông báo cho người thắng cuộc đấu giá để thanh toán đơn hàng. | ❌ CHƯA CÓ | Như trên |
| `ShipmentCreatedEvent` | `FulfillmentService` | Tạo thông báo đơn hàng đã xuất kho kèm mã vận đơn `TrackingCode`. | ❌ CHƯA CÓ | Như trên |
| `ShipmentStatusUpdatedEvent` | `FulfillmentService` | Tạo thông báo kiện hàng đã tới kho trung chuyển hoặc đang đi giao. | ❌ CHƯA CÓ | Như trên |
| `ShipmentDeliveredEvent` | `FulfillmentService` | Tạo thông báo kiện hàng đã được giao thành công. | ❌ CHƯA CÓ | Như trên |

> **Đếm thật: 1/9 sự kiện có consumer.** 9 dòng còn lại giữ nguyên như **thiết kế đích** để không mất hợp đồng mong muốn, nhưng tuyệt đối không được mô tả là đã hoạt động.

### 1.1. Chi tiết consumer duy nhất đang chạy

| Thuộc tính | Giá trị thật | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| Lớp | `public sealed class SendOtpEmailConsumer : IConsumer<SendOtpEmailIntegrationEvent>` | ✅ CÓ | `Infrastructure/Consumers/SendOtpEmailConsumer.cs:9` |
| Hợp đồng event | `record SendOtpEmailIntegrationEvent(string Email, string FullName, string Otp, int ExpiresInMinutes = 5)`, namespace `Nexus.Contracts.Events` | ⚠️ TRÙNG KHAI BÁO | `Application/Contracts/SendOtpEmailIntegrationEvent.cs:3-7` — bản sao của record cùng tên bên `User` service (cùng namespace, khác assembly) |
| Dependency inject | `IEmailSenderService` + `ILogger<SendOtpEmailConsumer>` | ✅ CÓ | `:11-20` |
| Ghi DB | **Không** — không inject `INotificationDbContext`, không gọi `SaveChangesAsync` | ❌ CHƯA CÓ | `:11-45`; `Application/Interfaces/INotificationDbContext.cs:8-21` được đăng ký (`DependencyInjection.cs:22`) nhưng 0 lần inject trong consumer |
| Kiểm tra preference trước khi gửi | **Không** — gọi sender trực tiếp | ❌ CHƯA CÓ | `:30-36` |
| Endpoint name | Kebab-case (`send-otp-email`) | ✅ CÓ | `DependencyInjection.cs:35` `SetKebabCaseEndpointNameFormatter()` |
| Publish (chiều ra) | **Không có publisher nào** trong repo | ❌ CHƯA CÓ | Không có `IPublishEndpoint`/`IBus` trong `Infrastructure/` hay `Application/` |

---

## 2. QUY TẮC IDEMPOTENT HANDLING TRÁNH SPAM THÔNG BÁO

**Đặc tả:** dùng MassTransit Inbox Pattern — mỗi sự kiện mang `MessageId`; nếu message được gửi lại do timeout mạng, consumer phát hiện trong bảng `InboxState` và bỏ qua, đảm bảo không gửi 2 email/push trùng lặp.

**Hiện trạng thật:**

| Hạng mục | Trạng thái | Bằng chứng (file:line) |
|:---|:---:|:---|
| Cấu hình EF Outbox (`UsePostgres` + `UseBusOutbox`) | ⚠️ CÓ CẤU HÌNH, CHƯA KIỂM CHỨNG | `Infrastructure/DependencyInjection.cs:37-41` `AddEntityFrameworkOutbox<AppDbContext>` |
| Bảng `InboxState` | ✅ TẠO (nhưng consumer không ghi DB nên gần như vô dụng) | Migration `Migrations/20261024103253_InitialNotificationSchema.cs:107`; `AppDbContext.cs:32` `AddInboxStateEntity()` |
| Bảng `OutboxMessage` / `OutboxState` | ✅ TẠO | Migration `:225`, `:153`; `AppDbContext.cs:33-34` |
| Idempotency thực thi trong consumer | ❌ CHƯA CÓ | `SendOtpEmailConsumer.cs:22-45` không có check `MessageId`, không dedup |
| Retry policy (`UseMessageRetry`) | ❌ CHƯA CÓ | `DependencyInjection.cs:43-58` chỉ `UsingRabbitMq(... host ... ConfigureEndpoints)` |
| Delayed redelivery / DLQ (`*_error`, `*_skipped`) | ❌ CHƯA CÓ | Không có `UseDelayedRedelivery`/`UseMessageScheduler`; MassTransit chỉ tạo queue lỗi mặc định nếu có retry |
| Test cho đường event | ⚠️ 1 CASE, KHÔNG CHẠM DB | `Notification.Tests/EmailSenderTests.cs:40-68` (Moq verify `SendOtpRegistrationEmailAsync` gọi 1 lần) |

> **Kết luận:** mục §2 là **đặc tả chưa đạt**. Không được viết "hệ thống chống trùng lặp" trong bất kỳ tài liệu nào cho tới khi consumer thực sự ghi `InboxState` và có test chứng minh.
