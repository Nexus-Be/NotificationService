# Tích hợp thông báo Buy Now và Giá sàn

## Kế hoạch và đặc tả

Nguồn bàn giao là NOTIFICATION_HANDOFF_BUY_NOW_AND_RESERVE.md. Auction dev đã cập nhật 0f76028 trong lúc đối chiếu. Dùng REST qua Gateway và danh tính JWT Auction hiện có.

- [x] Pull Auction dev và giữ bản dự phòng sửa đổi local.
- [x] Đối chiếu trạng thái thực tế trong BuyNowAsync, ApproveBelowReserveAsync, RejectBelowReserveAsync và worker hết hạn.
- [x] Thêm API nhắc duyệt dưới giá sàn và cảnh báo/khóa quyền đặt Giá sàn. Triển khai in-app trong phạm vi hiện tại.
- [x] Nối caller trước cùng lần lưu trạng thái nghiệp vụ của Auction.
- [x] Giữ ba hợp đồng có sẵn cho người thắng, đơn đã thanh toán và tiền cọc. Sửa tham chiếu winningBidId của caller Buy Now nếu khác bid thực tế.
- [x] Kiểm tra JWT, quyền sở hữu inbox, replay, gửi đồng thời và trạng thái đọc.
- [x] Kiểm tra phiên dưới giá sàn chờ 12 giờ, Seller duyệt/từ chối và quá hạn, cảnh báo lần 2 và hạn chế từ lần 3 trong 14 ngày.
- [x] Kiểm tra Buy Now qua các API nghiệp vụ với Wallet/Catalog giả trong môi trường DB riêng. Ghi rõ phần phụ thuộc Commerce/Wallet cần nghiệm thu thêm.
- [x] Cập nhật contract, kết quả test và hướng kiểm tra demo.

## Contract dự kiến

| Endpoint POST dưới /api/v1/notifications | Người nhận | Trường nghiệp vụ |
|---|---|---|
| auction-reserve-approval-needed | sellerUserId | sourceEventId, auctionId, sellerUserId, auctionTitle, highestBidAmount, reservePriceAmount, currency, approvalDeadlineAt, traceId tùy chọn |
| auction-reserve-restriction | sellerUserId | sourceEventId, sellerUserId, consecutiveDeclineCount, isRestricted, restrictedUntil, occurredAt, traceId tùy chọn |

Nhắc duyệt chỉ phát khi settlement vừa vào AwaitingSellerApproval. Mốc hạn lấy từ SellerApprovalDeadlineAt đã tính ở Auction. Cảnh báo dùng count=2, isRestricted=false và restrictedUntil=null. Hạn chế dùng count>=3, isRestricted=true và restrictedUntil sau occurredAt. Auction chịu trách nhiệm tính 12 giờ/14 ngày và kiểm soát quyền.

Notification trả 200 gồm sourceEventId, isReplay và notificationIds. Gửi lại cùng snapshot giữ cùng ID; đổi dữ liệu cùng sự kiện trả 409. API mới dùng JWT scope auction.notifications.write, issuer auction-service và audience nexus-auction-notifications.

Kiểm thử dùng PostgreSQL riêng và dữ liệu thử. Trạng thái hoàn thành được ghi theo từng ca. Giao dịch ví/provider thật và thao tác FE được ghi riêng nếu chưa kiểm chứng.

## Kết quả kiểm chứng

- Build Release: Auction, Notification, Gateway thành công. Auction còn 5 cảnh báo nullable của code hiện có.
- Toàn bộ Notification: 807 PASS, 0 FAIL, 0 SKIP, PostgreSQL Docker riêng.
- Gateway: 58 PASS, 0 FAIL, 0 SKIP cho danh tính, bất biến kiến trúc và cấu hình route.
- Luồng HTTP thực tế: Auction → Gateway → Notification → inbox Seller/Buyer trên DB riêng. Wallet/Catalog giả lập; User check được bypass riêng trong môi trường thử. Worker gửi email/push tắt.
- Không thay dữ liệu/schema Neon, không đổi SMTP/Firebase. Không commit hoặc push các thay đổi mới.

## Cần nghiệm thu trên môi trường của nhóm

- [ ] Backup và đối chiếu migration Buy Now/Giá sàn mới của Auction trước khi áp dụng DB dùng chung. Notification không có migration mới cho hai API này.
- [ ] Build lại image Auction, Gateway, Notification để runtime dùng code mới; giữ cấu hình riêng và key ring OTP hiện có.
- [ ] Đăng nhập Seller/Buyer trên FE, đối chiếu badge và nội dung; quyết định duyệt phải kiểm tra trạng thái/hạn gốc của phiên.
- [ ] Chạy thanh toán phí giao hàng qua Commerce/Wallet thật, xác nhận order-payments và các thay đổi tiền cọc sau commit.
- [ ] Triển khai Email/Push mới theo yêu cầu riêng ở giai đoạn sau.
