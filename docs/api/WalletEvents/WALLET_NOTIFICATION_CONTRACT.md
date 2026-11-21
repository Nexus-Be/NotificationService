# Contract nhận sự kiện Wallet — Phase 2 D-10a

Ngày: 20/11/2026. Phạm vi hiện tại: **in-app**, không gửi Email/Push mới. Dữ liệu do Wallet xác nhận sau commit. Notification không chuyển tiền, mở khóa số dư hoặc suy luận giao dịch thành công.

## Vận chuyển và danh tính

Wallet gọi trực tiếp Notification trong mạng backend tin cậy, JSON qua HTTPS khi vượt ranh giới máy/mạng. Hai POST:

- `/api/v1/notifications/withdrawal-events`
- `/api/v1/notifications/auction-deposits`

Caller Wallet hiện gửi `X-Internal-Service-Key`. Receiver mới dùng scheme `WalletNotifications`, chỉ có hiệu lực với hai POST này. Không bật lại hoặc nới lỏng middleware `/internal` toàn cục. Không yêu cầu Wallet tự giả lập `x-user-id`/`x-user-roles`.

| Bên | Cấu hình |
|---|---|
| Wallet caller | `NOTIFICATION_SERVICE_KEY` hoặc `Services:Notification:ServiceKey` theo client nguồn |
| Notification chạy .NET | `WalletNotificationAuth:ServiceKey` trong cấu hình riêng, hoặc biến môi trường `WalletNotificationAuth__ServiceKey` |
| Notification Docker Compose | `.env` riêng: `WALLET_NOTIFICATION_SERVICE_KEY`, được ánh xạ sang `WalletNotificationAuth__ServiceKey` |

Hai bên dùng cùng giá trị riêng, tối thiểu 32 byte UTF-8. Không có giá trị mặc định được chấp nhận. Thiếu/sai/quá ngắn trả 401; không tự chuyển sang xác thực bằng header người dùng. Không gửi cả Bearer và key trong cùng request Wallet. Không đưa key lên Git/FE/log.

Gateway hiện có thể chặn header này theo chính sách chống giả mạo; **không kết luận đi qua Gateway được**. Hợp đồng Wallet hiện tại dùng kết nối backend trực tiếp. Muốn đi Gateway cần đồng bộ chính sách S2S riêng; không tắt chống giả mạo.

## 1. Kết quả rút tiền

```json
{
  "sourceEventId": "withdrawal:10000000-0000-4000-8000-000000000001:Completed",
  "withdrawalId": "10000000-0000-4000-8000-000000000001",
  "recipientUserId": "20000000-0000-4000-8000-000000000001",
  "status": "Completed",
  "confirmedAt": "2026-11-20T15:00:00Z",
  "displayReason": null,
  "transactionId": "BANK-TEST-001",
  "traceId": "withdrawal-demo"
}
```

Chấp nhận `Approved`, `Rejected`, `Completed`, `Failed`. Không nhận `Pending`/`Processing` như kết quả đã xác nhận. Không bắt buộc số tiền vì DTO Wallet hiện tại không gửi trường này; Notification không tự điền tiền hoặc số tài khoản ngân hàng.

`sourceEventId` là chuỗi không rỗng, tối đa 200 ký tự, không phải UUID bắt buộc. Withdrawal/recipient là UUID khác rỗng. `confirmedAt` khác mặc định, ISO 8601 có múi giờ. `displayReason` tùy chọn, tối đa 1.000 ký tự, phải an toàn để hiển thị cho chủ yêu cầu. `transactionId` tùy chọn tối đa 200; trace tùy chọn tối đa 100. Không gửi ghi chú nội bộ hoặc bằng chứng ngân hàng trong displayReason.

Type trả ra: `WithdrawalEvent`, DB integer **25**. Thông báo thuộc recipientUserId.

## 2. Biến động cọc của Wallet

```json
{
  "sourceEventId": "hold:30000000-0000-4000-8000-000000000001:Released",
  "transactionId": "30000000-0000-4000-8000-000000000001",
  "auctionId": "40000000-0000-4000-8000-000000000001",
  "bidderId": "20000000-0000-4000-8000-000000000001",
  "status": "Released",
  "amount": 500000,
  "currency": "VND",
  "confirmedAt": "2026-11-20T15:00:00Z",
  "traceId": "deposit-demo"
}
```

Chấp nhận `Held`, `Released`, `Captured`, `Forfeited`, `Refunded`. Bidder là UUID khác rỗng; transactionId là chuỗi không rỗng tối đa 200. AuctionId là tham chiếu chuỗi tùy chọn tối đa 200, giữ đúng DTO nguồn; không mặc định mọi chuỗi đều dùng được làm URL chi tiết phiên. Amount >0, tối đa hai số thập phân; currency=VND. ConfirmedAt/trace theo quy tắc trên. Giới hạn body mỗi endpoint: 16 KiB; trường không thuộc DTO bị từ chối.

Type dùng lại `AuctionDeposit`, DB **22**, không thêm loại cọc thứ hai.

## 3. Cùng URL, hai hợp đồng cọc

Endpoint `/auction-deposits` chỉ có **một action**. Danh tính đã xác thực quyết định DTO:

| Caller | Xác thực | DTO/trạng thái |
|---|---|---|
| Auction | JWT Auction hiện có, scope `auction.notifications.write` | UUID sourceEventId, depositId, bidderUserId, auctionTitle; Authorized/Released/Captured |
| Wallet | Wallet key nêu trên | Chuỗi sourceEventId, transactionId, bidderId, không cần auctionTitle; năm trạng thái Wallet |

Không chọn caller bằng trường trong body. Không chấp nhận payload Wallet với JWT Auction hoặc trộn hai schema. Response Auction giữ nguyên sourceEventId/isReplay/notificationIds. Swagger mô tả request `oneOf` hai DTO và hai lựa chọn xác thực.

## 4. Response và replay Wallet

HTTP 200:

```json
{
  "notificationId": "50000000-0000-4000-8000-000000000001",
  "sourceEventId": "hold:30000000-0000-4000-8000-000000000001:Released",
  "isReplay": false
}
```

Caller Wallet hiện có đọc được notificationId ở root. Cùng sourceEventId và cùng snapshot trả cùng notificationId, isReplay=true, không tạo delivery/audit mới. Trace không thuộc hash; confirmedAt được chuẩn hóa UTC. Thay người nhận/trạng thái/tiền/ID nghiệp vụ với cùng event ID trả **409 SERVICE_EVENT_CONFLICT**.

Notification ánh xạ `wallet:{sourceEventId}` bằng SHA-256 lấy 16 byte thành GUID .NET cho cột SourceEventId hiện có. Payload giữ chuỗi gốc để đối soát; caller không cần tính GUID này. Không đổi schema hoặc thêm bảng.

Transaction + advisory lock theo sự kiện bảo vệ replay đồng thời; notification, InApp delivery và audit `ServiceEventAccepted` (30) cùng commit. Receipt ẩn/xóa mềm vẫn được dùng khi replay. Kênh bị tắt vẫn lưu receipt, delivery Skipped và không hiện inbox. Sent của InApp chỉ là đã lưu/hiển thị được, không chứng minh người dùng đã đọc.

400: dữ liệu sai; 401: thiếu/sai credential; 403: danh tính Auction/scope không được phép; 409: conflict hoặc thiếu cấu hình kênh. Lỗi gửi thông báo không được hoàn tác giao dịch tài chính đã commit. Khi thử lại phải giữ payload/confirmedAt/event ID, không lấy thời gian mới để né conflict.

## 5. Chốt bên phát để tránh trùng nghiệp vụ

Replay chỉ chống trùng **cùng ID sự kiện**. Wallet và Auction hiện không gửi cùng hold/deposit identifier và event ID; receiver không thể kết luận hai sự kiện khác ID là cùng một giao dịch.

Khuyến nghị Wallet làm nguồn phát thông báo biến động tiền cọc đã xác nhận. Auction giữ các thông báo kết quả phiên, thắng, duyệt giá sàn và lời mời thay thế. Nhóm cần ngừng caller cọc tương ứng ở bên còn lại hoặc cung cấp ID đối chiếu chung. Trước khi chốt, chưa đánh dấu “không trùng xuyên service” là PASS.

## 6. Checklist nghiệm thu

- [ ] Đồng bộ key riêng và địa chỉ backend trực tiếp; không dùng key mẫu.
- [ ] Wallet phát từ thao tác rút tiền/hold thật sau commit; kiểm tra không còn 405/401.
- [ ] Đối chiếu recipient, transaction/withdrawal, event ID, notificationId và inbox.
- [ ] Replay và đổi tài khoản: không thêm thông báo, không xem dữ liệu người khác.
- [ ] Chốt duy nhất một bên phát cho từng thay đổi cọc.
- [ ] Email/Push và nghiệm thu thiết bị thực hiện ở giai đoạn riêng.
