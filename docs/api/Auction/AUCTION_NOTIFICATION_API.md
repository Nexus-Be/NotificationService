# API thông báo đấu giá

Tài liệu dành cho bên tích hợp AuctionService, Gateway và FE. Đường dẫn trong bảng dùng tiền tố `/api/v1/notifications`. NotificationService lưu và phân phối thông báo. AuctionService quyết định trạng thái phiên, người thắng, điều kiện mời thay thế và hạn phản hồi.

## Danh sách endpoint

| POST | Nghiệp vụ | Tài liệu chi tiết |
|---|---|---|
| `/auction-reserve-approval-needed` | Seller duyệt giá dưới giá sàn | [Buy Now và Giá sàn](../AuctionReserve/AUCTION_BUY_NOW_RESERVE_CONTRACT.md) |
| `/auction-reserve-restriction` | Cảnh báo hoặc hạn chế Giá sàn | [Buy Now và Giá sàn](../AuctionReserve/AUCTION_BUY_NOW_RESERVE_CONTRACT.md) |
| `/auction-failed` | Báo Seller khi phiên chuyển sang thất bại | [Lifecycle in-app](#lifecycle-in-app) |
| `/auction-cancelled` | Báo Seller khi phiên bị hủy bởi Seller hoặc Admin | [Lifecycle in-app](#lifecycle-in-app) |
| `/auction-winner-offers` | Mời bidder đủ điều kiện làm người thắng thay thế | [Lifecycle in-app](#lifecycle-in-app) |
| `/auction-outbids` | Báo người vừa bị vượt giá | [Auction outbids](../AuctionOutbids/AUCTION_OUTBIDS.md) |
| `/auction-winners` | Báo người thắng phiên | [Auction winners](../AuctionWinners/AUCTION_WINNERS.md) |
| `/auction-starts` | Báo phiên bắt đầu | [Auction starts](../AuctionStarts/AUCTION_STARTS.md) |
| `/auction-seller-results` | Báo kết quả phiên cho Seller | [Auction seller results](../AuctionSellerResults/AUCTION_SELLER_RESULTS.md) |
| `/auction-cancellations` | Hợp đồng hủy phiên hiện có theo danh sách người nhận | [Auction cancellations](../AuctionCancellations/AUCTION_CANCELLATIONS.md) |
| `/auction-order-payments` | Báo thanh toán đơn đấu giá | [Auction order payments](../AuctionOrderPayments/AUCTION_ORDER_PAYMENTS.md) |
| `/auction-payment-timeouts` | Báo hết hạn thanh toán | [Auction payment timeouts](../AuctionPaymentTimeouts/AUCTION_PAYMENT_TIMEOUTS.md) |
| `/auction-seller-payment-timeouts` | Báo hết hạn thanh toán cho Seller | [Auction seller payment timeouts](../AuctionSellerPaymentTimeouts/AUCTION_SELLER_PAYMENT_TIMEOUTS.md) |

Các endpoint lifecycle, auction-outbids, auction-winners, auction-order-payments và hai route Giá sàn mới trong [Buy Now và Giá sàn](../AuctionReserve/AUCTION_BUY_NOW_RESERVE_CONTRACT.md), cùng ba route trong [Auction activity](../AuctionActivity/AUCTION_ACTIVITY.md) dùng JWT service chuyên biệt. Các endpoint còn lại có quy tắc xác thực và trạng thái tích hợp riêng theo tài liệu từng route. Sự kiện tạo phiên chưa phát thông báo.

## Lifecycle in-app

### Xác thực và vận chuyển

AuctionService gửi POST qua Gateway với `Content-Type: application/json` và `Authorization: Bearer <auction-service-token>`. Gateway chuyển tiếp JWT sang NotificationService. NotificationService xác thực JWT độc lập cho ba route lifecycle.

| Thuộc tính | Giá trị |
|---|---|
| Thuật toán | HS256, khóa riêng dùng chung tối thiểu 32 byte UTF-8 |
| Issuer | `auction-service` |
| Audience | `nexus-auction-notifications` |
| Subject | GUID service được cấu hình đồng nhất trên Auction, Gateway và Notification |
| Scope | `auction.notifications.write` |
| Thời hạn | Auction phát token 2 phút. Notification cho phép độ lệch đồng hồ 15 giây. |

Xem [hợp đồng tích hợp](../../integrations/NOTIFICATION_AUCTION_LIFECYCLE_CONTRACT.md) để đồng bộ biến môi trường Docker và cách sinh ID sự kiện ở Auction. Trình duyệt dùng phiên của người dùng để đọc inbox. Thông tin xác thực service được giữ ở backend.

### Request JSON

Ba request đều yêu cầu `sourceEventId`, `auctionId`, `auctionTitle` và cho phép `traceId`. Dùng UUID khác rỗng, tiêu đề có tối đa 200 ký tự, trace tối đa 100 ký tự và timestamp ISO 8601 có múi giờ.

| Endpoint | Trường bổ sung | Điều kiện |
|---|---|---|
| `/auction-reserve-approval-needed` | Seller duyệt giá dưới giá sàn | [Buy Now và Giá sàn](../AuctionReserve/AUCTION_BUY_NOW_RESERVE_CONTRACT.md) |
| `/auction-reserve-restriction` | Cảnh báo hoặc hạn chế Giá sàn | [Buy Now và Giá sàn](../AuctionReserve/AUCTION_BUY_NOW_RESERVE_CONTRACT.md) |
| `/auction-failed` | `sellerUserId`, `reason`, `failedAt` | Lý do bắt buộc, tối đa 1.000 ký tự. Phát sau khi trạng thái thất bại đã lưu. |
| `/auction-cancelled` | `sellerUserId`, `reason`, `cancelledAt`, `forced` | Lý do bắt buộc, tối đa 1.000 ký tự. `forced` là boolean bắt buộc, `true` khi Admin cưỡng chế hủy. |
| `/auction-winner-offers` | `offerId`, `bidId`, `bidderUserId`, `previousWinnerUserId`, `sellerUserId`, `amount`, `currency`, `offeredAt`, `expiresAt` | `amount > 0`, `currency = "VND"`, `expiresAt > offeredAt`. Bidder được mời phải khác người thắng trước và Seller. |

Ví dụ request `POST /api/v1/notifications/auction-cancelled` với ID minh họa:

```json
{
  "sourceEventId": "10000000-0000-4000-8000-000000000001",
  "auctionId": "20000000-0000-4000-8000-000000000001",
  "sellerUserId": "30000000-0000-4000-8000-000000000001",
  "auctionTitle": "Máy ảnh",
  "reason": "Seller hủy phiên.",
  "cancelledAt": "2026-11-17T03:30:00Z",
  "forced": false,
  "traceId": "auction-demo-01"
}
```

### Kết quả và gửi lại

HTTP `200` từ NotificationService trả `sourceEventId` và `notificationId`. Cả ba sự kiện được lưu vào inbox với kênh In-app. Email và Push cho nhóm lifecycle này nằm ngoài phạm vi hiện tại.

| HTTP | Ý nghĩa |
|---|---|
| `400` | Dữ liệu thiếu hoặc sai điều kiện |
| `401` | JWT thiếu, hết hạn hoặc không hợp lệ |
| `403` | JWT hợp lệ nhưng sai subject hoặc thiếu scope |
| `409` | ID sự kiện đã gắn với loại thông báo hoặc người nhận khác |

Gửi lại cùng `sourceEventId` và cùng payload. Với loại và người nhận khớp, API trả ID thông báo đã có. Auction có thể thử lại khi gặp timeout, `408`, `429` hoặc `5xx`. Với lỗi dữ liệu hoặc quyền, kiểm tra request và cấu hình trước khi gửi lại.

### Đọc inbox và kiểm thử qua FE

1. Tạo phiên thử rồi đi qua từng nhánh hủy, thất bại và mời thay thế từ nghiệp vụ Auction.
2. So khớp `sourceEventId` và `notificationId` giữa phản hồi POST và kết quả `GET /api/v1/notifications/me` của người nhận.
3. Đăng nhập Seller để xem hai sự kiện hủy/thất bại và đăng nhập đúng bidder để xem lời mời. Thử đọc thông báo bằng tài khoản khác để kiểm tra quyền sở hữu.
4. Gửi lại cùng sự kiện và kiểm tra inbox chỉ có một mục tương ứng. Đánh dấu đã đọc rồi tải lại trang để kiểm tra trạng thái.
5. Dùng `auctionId` và `offerId` trong payload để gọi `POST /api/v1/auctions/{auctionId}/winner-offers/{offerId}/accept` hoặc `/decline` bằng phiên bidder. Sau khi Auction xử lý, tải lại chi tiết phiên để xem kết quả.

Nguồn đối chiếu trong repo gồm [controller](../../../Notification.Api/Controllers/Public/AuctionLifecycleController.cs), [DTO request](../../../Notification.Application/Services/AuctionLifecycle/AuctionLifecycleRequests.cs) và [logic lưu thông báo](../../../Notification.Application/Services/AuctionLifecycle/AuctionLifecycleService.cs).
