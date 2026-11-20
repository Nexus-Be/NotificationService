# Hợp đồng Notification: Buy Now và Giá sàn

Ngày đối chiếu: 20/11/2026. Nguồn nghiệp vụ: bản bàn giao `NOTIFICATION_HANDOFF_BUY_NOW_AND_RESERVE.md` và Auction dev `0f76028`. Hai endpoint mới triển khai **in-app**; Email/Push được xử lý ở giai đoạn sau.

## Phân công và luồng

Auction quyết định giá, người nhận, trạng thái, hạn duyệt 12 giờ và thời gian hạn chế 14 ngày. Notification lưu thông báo cá nhân và delivery InApp, không thay đổi phiên hoặc tự khóa tài khoản. Hạn chế Giá sàn không phải ban tài khoản.

Caller đưa snapshot và ID ổn định vào cơ chế gửi bền vững đang có của Auction trong cùng lần lưu trạng thái nghiệp vụ. Dispatcher gọi REST qua Gateway sau commit. Không gọi Notification trước rồi mới lưu nghiệp vụ. Notification không bổ sung broker hoặc bảng outbox/inbox.

| Mốc nghiệp vụ | Endpoint POST `/api/v1/notifications/...` | Người nhận |
|---|---|---|
| Buy Now thành công | `auction-winners` hiện có | Buyer |
| Settlement vừa chuyển AwaitingSellerApproval | `auction-reserve-approval-needed` mới | Seller |
| Seller chấp nhận bán dưới giá sàn | `auction-winners` hiện có | Người thắng |
| Seller từ chối hoặc hết hạn duyệt | `auction-failed` hiện có, lý do ReserveNotMet | Seller |
| Lần từ chối liên tiếp thứ 2 | `auction-reserve-restriction` mới, cảnh báo | Seller |
| Từ lần thứ 3, kể cả timeout | `auction-reserve-restriction` mới, hạn chế 14 ngày | Seller |
| Đơn đã được nguồn xác nhận thanh toán | `auction-order-payments` hiện có | Theo hợp đồng thanh toán |
| Cọc được xử lý thành công | `auction-deposits` hiện có | Chủ cọc |

Buy Now dùng `winningAmount = BuyNowPriceAmount`, `winningBidId` của Bid được tạo thật, không dùng Result ID thay Bid ID. `paymentDeadline` lấy từ settlement +24 giờ. Thông báo thắng/mua ngay không phải bằng chứng đã hoàn tất thanh toán đơn. Không phát winner khi đang chờ Seller duyệt. Duyệt giá sàn mới bắt đầu hạn thanh toán 24 giờ.

## Xác thực

POST JSON kèm `Authorization: Bearer <service JWT>`. HS256; issuer `auction-service`; audience `nexus-auction-notifications`; subject là service GUID đã cấu hình; scope `auction.notifications.write`. Gateway và Notification kiểm tra riêng. Khóa ký phải đồng nhất, gửi riêng trong cấu hình, không đưa vào tài liệu/FE. Token của người dùng không được phát sự kiện; JWT service không được đọc inbox cá nhân.

## 1. Yêu cầu Seller duyệt giá

```json
{
  "sourceEventId": "10000000-0000-4000-8000-000000000001",
  "auctionId": "20000000-0000-4000-8000-000000000001",
  "sellerUserId": "30000000-0000-4000-8000-000000000001",
  "auctionTitle": "Máy ảnh",
  "highestBidAmount": 14000000,
  "reservePriceAmount": 15000000,
  "currency": "VND",
  "approvalDeadlineAt": "2026-11-21T03:00:00Z",
  "traceId": "reserve-demo-01"
}
```

UUID khác rỗng; tiêu đề 1–200 ký tự; các giá trị tiền >0, tối đa 2 chữ số thập phân; giá cao nhất thấp hơn giá sàn; tiền tệ VND; deadline ISO 8601 có múi giờ, không mặc định; trace tùy chọn, tối đa 100 ký tự. Thời hạn lấy từ Auction, không tính lại khi retry. Sự kiện đến muộn vẫn được lưu đúng deadline gốc; FE đọc trạng thái phiên hiện tại trước khi cho phản hồi.

Thông báo `AuctionReserveApprovalNeeded` (giá trị DB 23) lưu riêng cho Seller, có giá cao nhất, giá sàn và hạn phản hồi theo giờ Việt Nam. Payload chứa `auctionId`, `details.highestBidAmount`, `details.reservePriceAmount`, `details.currency`, `details.approvalDeadlineAt`.

## 2. Cảnh báo/hạn chế Giá sàn

```json
{
  "sourceEventId": "10000000-0000-4000-8000-000000000002",
  "sellerUserId": "30000000-0000-4000-8000-000000000001",
  "consecutiveDeclineCount": 2,
  "isRestricted": false,
  "restrictedUntil": null,
  "occurredAt": "2026-11-20T15:00:00Z",
  "traceId": "reserve-demo-02"
}
```

Cảnh báo: count=2, `isRestricted=false`, `restrictedUntil=null`. Hạn chế: count>=3, `isRestricted=true`, `restrictedUntil > occurredAt`; Auction tính +14 ngày. Cờ boolean bắt buộc. Các UUID/thời gian không mặc định. Trường thừa bị từ chối. Đây là sự kiện theo Seller, không cần `auctionId` hoặc tiêu đề phiên trong request.

Thông báo `AuctionReserveRestriction` (DB 24) lưu riêng cho Seller. Payload `auctionId=null`, `details` chứa số lần, trạng thái hạn chế và thời điểm liên quan. FE không điều hướng seller-wide event vào chi tiết phiên với ID rỗng.

## Response, replay và lỗi

HTTP 200 với DTO gốc (Gateway có thể bọc envelope chung):

```json
{
  "sourceEventId": "10000000-0000-4000-8000-000000000001",
  "isReplay": false,
  "notificationIds": ["40000000-0000-4000-8000-000000000001"]
}
```

HTTP 200 nghĩa là đã lưu thông báo, không chứng minh thiết bị nhận Push hoặc thư đã đến hộp thư.

- Cùng ID + cùng snapshot: trả lại notification IDs, `isReplay=true`; không tạo thêm thông báo/delivery. Trace không thuộc dữ liệu so sánh; timestamp được chuẩn hóa UTC.
- Cùng ID + dữ liệu nghiệp vụ khác: HTTP 409 `AUCTION_ACTIVITY_CONFLICT`; không ghi đè thông báo.
- Dữ liệu sai: 400. Thiếu/sai/hết hạn JWT: 401. Danh tính/scope không được phép: 403.
- Giao dịch PostgreSQL và khóa theo SourceEventId bảo vệ replay đồng thời. Notification vẫn giữ receipt qua bản ghi thông báo bị ẩn/xóa mềm.
- Auction retry timeout/lỗi tạm thời với nguyên payload và event ID. Không tạo ID mới để né 409; đối soát sự kiện gốc.

ID caller hiện tại dùng DeterministicGuid (MD5 UTF-8 thành Guid theo .NET) từ `auction-reserve-approval:{auctionId:N}:{approvalDeadlineAt:O}` hoặc `auction-reserve-restriction:{sellerUserId:N}:{count}:{occurredAt:O}`. Snapshot được serialize một lần trong hàng chờ và giữ nguyên khi retry.

## FE và nghiệm thu

FE đọc `/api/v1/notifications/me` qua Gateway bằng phiên người dùng, hỗ trợ badge/đọc/ẩn theo API inbox hiện có. Cần hiển thị được hai type mới mà không phụ thuộc danh sách enum cứng. Notification chỉ trả thông báo của người đang đăng nhập.

Ca demo: hết phiên dưới giá sàn → Seller thấy yêu cầu duyệt → duyệt thì Buyer nhận winner; từ chối thì Seller nhận Failed. Từ chối hai lần nhận cảnh báo, lần ba hoặc timeout tương đương nhận hạn chế. Buy Now thành công nhận winner có đúng giá, Bid ID và deadline; retry không nhân bản.

Phải nghiệm thu thêm trên FE và Wallet/Commerce thật trước khi kết luận toàn luồng tài chính hoàn tất. Test DB riêng, Wallet/Catalog giả lập không chứng minh đã chuyển tiền thật.
