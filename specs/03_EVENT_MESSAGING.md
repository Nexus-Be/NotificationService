# 03 — SỰ KIỆN BẤT ĐỒNG BỘ QUA RABBITMQ (NOTIFICATION SERVICE EVENT MESSAGING)

NotificationService là **Subscriber trung tâm** nhận sự kiện từ hầu hết các service trong hệ sinh thái.

---

## 1. SỰ KIỆN LẮNG NGHE & HÀNH ĐỘNG TƯƠNG ỨNG

| Sự Kiện Nhận Được | Service Phát Hành | Hành Động Tạo Thông Báo Cho Khách Hàng |
|:---|:---:|:---|
| `UserRegisteredEvent` | `UserService` | Tạo thông báo chào mừng thành viên mới, gửi email kích hoạt. |
| `SellerApprovedEvent` | `UserService` | Tạo thông báo chúc mừng tài khoản đã được nâng cấp thành Seller. |
| `OrderPaidEvent` | `CommerceService` | Tạo thông báo xác nhận thanh toán thành công, đơn hàng đang được chuẩn bị. |
| `OrderCancelledEvent` | `CommerceService` | Tạo thông báo đơn hàng đã bị hủy kèm lý do chi tiết. |
| `BidPlacedEvent` | `AuctionService` | Tạo thông báo cho người giữ giá trước đó: "Bạn vừa bị đặt giá vượt qua (outbid)!". |
| `AuctionEndedEvent` | `AuctionService` | Tạo thông báo cho người thắng cuộc đấu giá để thanh toán đơn hàng. |
| `ShipmentCreatedEvent` | `FulfillmentService`| Tạo thông báo đơn hàng đã xuất kho kèm mã vận đơn `TrackingCode`. |
| `ShipmentStatusUpdatedEvent` | `FulfillmentService`| Tạo thông báo kiện hàng đã tới kho trung chuyển hoặc đang đi giao. |
| `ShipmentDeliveredEvent` | `FulfillmentService`| Tạo thông báo kiện hàng đã được giao thành công. |

---

## 2. QUY TẮC IDEMPOTENT HANDLING TRÁNH SPAM THÔNG BÁO

- Sử dụng MassTransit Inbox Pattern: Mỗi sự kiện đến mang theo `MessageId`. Nếu message bị gửi lại do timeout mạng, consumer sẽ phát hiện trong bảng `InboxState` và bỏ qua, đảm bảo **không bao giờ gửi 2 email hoặc 2 push notification trùng lặp cho khách hàng**.
