# Cập nhật Notification — thông báo hoạt động đấu giá và Giá sàn

Ngày cập nhật: **20/11/2026**. Code được công bố trên `dev` tại commit `4271324`.

Tài liệu này tổng hợp phần Notification đã triển khai, hợp đồng cần đồng bộ và giới hạn nghiệm thu. Đây là cập nhật phía nhận thông báo; pull Notification không tự cập nhật caller Auction hoặc Gateway.

## 1. Nghiệp vụ được bổ sung

| Nghiệp vụ | Người nhận | Endpoint POST dưới `/api/v1/notifications` | Loại thông báo |
|---|---|---|---|
| Phiên đang theo dõi có giá mới | Các watcher do nguồn cung cấp | `/auction-watchlist-bids` | `AuctionWatchlistBid` |
| Báo cáo phiên đã được xử lý | Các reporter do nguồn cung cấp | `/auction-report-resolved` | `AuctionReportResolved` |
| Cọc đã giữ, giải phóng hoặc thu | Chủ cọc | `/auction-deposits` | `AuctionDeposit` |
| Kết thúc phiên dưới giá sàn, cần Seller duyệt | Seller của phiên | `/auction-reserve-approval-needed` | `AuctionReserveApprovalNeeded` |
| Cảnh báo hoặc hạn chế quyền đặt giá sàn | Seller liên quan | `/auction-reserve-restriction` | `AuctionReserveRestriction` |

Năm endpoint mới tạo **in-app**: bản ghi thông báo và delivery InApp được lưu trong cùng giao dịch. Chúng không gửi Email/Push trong đợt cập nhật này. Cờ tắt kênh và tùy chọn người dùng được tôn trọng; khi thông báo không hiển thị, bản ghi vẫn giữ danh tính sự kiện để chống gửi trùng.

Giới hạn Giá sàn là quyền nghiệp vụ do Auction quản lý, không phải khóa tài khoản User. Notification không tự cộng số lần vi phạm, thay đổi giá, duyệt phiên hoặc chuyển tiền.

## 2. Buy Now và các API được tái sử dụng

| Mốc nghiệp vụ | Hợp đồng Notification |
|---|---|
| Buy Now được nguồn xác nhận thành công | `/auction-winners` hiện có |
| Seller chấp nhận bán dưới giá sàn | `/auction-winners` hiện có |
| Seller từ chối hoặc hết hạn duyệt | `/auction-failed` hiện có, lý do ReserveNotMet |
| Đơn đấu giá đã được nguồn xác nhận thanh toán | `/auction-order-payments` hiện có |
| Tiền cọc được xử lý thành công | `/auction-deposits` |

Không thêm API Buy Now riêng. Caller phải truyền `winningBidId` của lượt Bid thật, `winningAmount` bằng giá Buy Now và `paymentDeadline` lấy từ settlement. Hạn thanh toán 24 giờ thuộc Auction; Notification không tính lại hạn khi retry. Thông báo winner không chứng minh đơn hàng đã hoàn tất thanh toán.

Khi settlement đang AwaitingSellerApproval, chỉ phát yêu cầu Seller duyệt; chưa phát winner. Seller duyệt mới bắt đầu cửa sổ thanh toán. Thông báo cần hiển thị hạn gốc, còn thao tác duyệt phải kiểm tra trạng thái phiên hiện tại ở Auction.

## 3. Contract Giá sàn

### Yêu cầu duyệt

`POST /api/v1/notifications/auction-reserve-approval-needed`

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

Giá cao nhất phải thấp hơn giá sàn. Auction tính thời hạn duyệt 12 giờ và truyền deadline gốc. Sự kiện đến muộn vẫn giữ deadline, không tự kéo dài thời gian xử lý.

### Cảnh báo và hạn chế

`POST /api/v1/notifications/auction-reserve-restriction`

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

- Cảnh báo: count=2, isRestricted=false, restrictedUntil=null.
- Hạn chế: count>=3, isRestricted=true, restrictedUntil sau occurredAt. Auction tính thời gian 14 ngày.
- Seller chấp nhận bán có thể reset chuỗi từ chối theo nghiệp vụ nguồn; endpoint này không tự sửa dữ liệu đó.
- Sự kiện theo Seller không yêu cầu auctionId. Payload thông báo có auctionId=null; FE không được ghép liên kết chi tiết phiên với ID rỗng.

UUID không được rỗng. Giá trị tiền dương, tối đa hai số thập phân; currency=VND. Tiêu đề tối đa 200 ký tự, trace tùy chọn tối đa 100 ký tự. Timestamp dùng ISO 8601 có múi giờ. Request có trường không thuộc DTO bị từ chối.

## 4. Xác thực, phản hồi và chống gửi trùng

Caller gửi JSON với `Authorization: Bearer <service JWT>`. Token dùng HS256, issuer `auction-service`, audience `nexus-auction-notifications`, subject GUID service đã thống nhất và scope `auction.notifications.write`. Khóa ký tối thiểu 32 byte UTF-8; Gateway và Notification phải cấu hình cùng khóa/subject. Không đưa khóa service vào FE.

API mới trả DTO gốc; Gateway có thể bọc envelope chung:

```json
{
  "sourceEventId": "10000000-0000-4000-8000-000000000001",
  "isReplay": false,
  "notificationIds": ["40000000-0000-4000-8000-000000000001"]
}
```

| Kết quả | Ý nghĩa |
|---|---|
| 200, isReplay=false | Đã lưu sự kiện và thông báo tương ứng |
| 200, isReplay=true | Cùng ID/snapshot đã được tiếp nhận, trả lại kết quả cũ |
| 400 | Dữ liệu không hợp lệ |
| 401 | Thiếu JWT, JWT sai hoặc hết hạn |
| 403 | Subject hoặc scope không được phép |
| 409, AUCTION_ACTIVITY_CONFLICT | Dùng lại SourceEventId với dữ liệu nghiệp vụ khác |

Trace không thuộc snapshot so sánh; timestamp được chuẩn hóa UTC. Với các batch, đảo thứ tự hoặc lặp lại cùng tập người nhận không tạo sự kiện mới. Nhóm nhận tối đa 1.000 người dùng riêng biệt, body tối đa 128 KiB; nguồn phải chia batch lớn hơn bằng ID ổn định riêng cho từng batch.

Giao dịch PostgreSQL và khóa theo SourceEventId bảo vệ các yêu cầu đồng thời. Mỗi người nhận có thông báo riêng; payload cá nhân không chứa danh sách người nhận khác. Nội dung adminReason của kết quả báo cáo không được đưa vào payload người dùng; nguồn phải bảo đảm message an toàn cho người nhận.

HTTP 200 là đã lưu, không phải bằng chứng thư/Push đến thiết bị. Không cấp quyền phát sự kiện nghiệp vụ cho trình duyệt. JWT service không được dùng để đọc inbox của người dùng.

## 5. File chính đã thay đổi

| Thành phần | File/thư mục trong repo |
|---|---|
| API nhận năm sự kiện | `Notification.Api/Controllers/Public/AuctionActivityController.cs` |
| JWT allowlist | `Notification.Api/Extensions/AuctionNotificationAuth.cs` |
| DTO, validation, replay, tạo thông báo | `Notification.Application/Services/AuctionActivity/` |
| Đăng ký service | `Notification.Application/DependencyInjection.cs` |
| Loại thông báo | `Notification.Domain/Enums/NotificationType.cs` |
| Giữ tương thích danh mục preferences hiện có | `Notification.Application/Services/Notifications/UserNotificationService.Preferences.cs` |
| Test API trên PostgreSQL | `Notification.Tests/AuctionActivityApiIntegrationTests.cs` |

Giá trị enum DB mới là 20–24; các giá trị cũ được giữ. Không thêm bảng hoặc migration Notification trong đợt này. Không thay ba file cấu hình riêng, SMTP hoặc Firebase.

## 6. Bằng chứng kiểm thử và giới hạn

Kết quả trước khi công bố code `4271324`:

- Build Notification Release: 0 lỗi, 0 cảnh báo.
- Toàn bộ Notification.Tests với PostgreSQL thử riêng: **807 PASS, 0 FAIL, 0 SKIP**.
- Kiểm tra phía Gateway ở bản tích hợp local tại thời điểm đó: **58 PASS**; đây không phải khẳng định các sửa đổi Gateway đã được push.
- Luồng HTTP nguồn → Gateway → Notification → inbox: sáu ca PASS, gồm yêu cầu duyệt, cảnh báo lần hai, timeout/hạn chế 14 ngày, chấp nhận/reset chuỗi, từ chối lần ba và Buy Now đúng Bid ID/deadline/replay; ca đọc inbox cũng được đối chiếu.

Các ca nguồn dùng PostgreSQL riêng, Wallet/Catalog giả lập, User check bypass trong môi trường thử và worker Email/Push tắt. Không thay schema/dữ liệu Neon. Chưa nghiệm thu thao tác FE, giao dịch Wallet/Commerce thật hoặc gửi Email/Push cho năm endpoint mới.

## 7. Checklist cho nhóm tích hợp

- [ ] Pull Notification dev có commit 4271324 hoặc mới hơn; nhận cấu hình riêng và đặt đúng vị trí theo README.
- [ ] Đồng bộ caller Auction, route/xác thực Gateway cùng contract. Các bản sửa nằm ở máy phát triển chưa mặc nhiên có trên dev của hai repo đó.
- [ ] Build lại image Notification; kiểm tra health/ready và Swagger có năm endpoint.
- [ ] Dùng sự kiện thử đi từ nghiệp vụ nguồn; đối chiếu sourceEventId, notificationId và inbox đúng người.
- [ ] Gửi lại nguyên sự kiện: không tăng số thông báo; thay snapshot cùng ID: trả 409.
- [ ] Kiểm tra thiếu/sai JWT và scope; đăng xuất/đổi người dùng không lộ thông báo.
- [ ] Trên FE, hiển thị title/body của type mới, cập nhật badge và lưu trạng thái đọc; sự kiện hạn chế không có auctionId.
- [ ] Nghiệm thu riêng thanh toán đơn, phí vận chuyển và xử lý cọc với các service thật.
- [ ] Thực hiện Email/Push mới sau khi thống nhất phạm vi và cấu hình provider.

## Tài liệu liên quan

- [Danh mục API đấu giá](../api/Auction/AUCTION_NOTIFICATION_API.md)
- [Contract Watchlist, Report, Deposit](../api/AuctionActivity/AUCTION_ACTIVITY.md)
- [Contract Buy Now và Giá sàn](../api/AuctionReserve/AUCTION_BUY_NOW_RESERVE_CONTRACT.md)
- [Checklist triển khai](../integrations/AUCTION_BUY_NOW_RESERVE_CHECKLIST.md)
- [Báo cáo kiểm thử](../integrations/AUCTION_BUY_NOW_RESERVE_TEST_REPORT.md)
