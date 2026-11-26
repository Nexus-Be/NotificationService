# Caller cần bổ sung để nghiệm thu Notification toàn dự án

Ngày 25/11/2026. Đây là yêu cầu tích hợp từ code hiện có; không dùng request Notification thủ công để đánh PASS flow của service nguồn.

## Fulfillment — ưu tiên cao

Caller hiện tại POST api/v1/internal/notifications/delivery-alert trả 404. Email/send đã có hỗ trợ xác thực khóa mạnh nhưng là legacy email, không thay thế intake nghiệp vụ.

Đích chính thức: POST /api/v1/notifications/shipment-events. Snapshot cần sourceEventId, shipmentId, sourceVersion >0 được lưu bền vững, orderId, shopId thực (SellerProfile, không phải SellerUserId), buyerUserId, sellerUserId, orderKind Regular/Auction, auctionId cho Auction, orderNo, carrier GHN, trackingCode, status, reason cho thất bại/trả/hủy, paidAt, sellerAcceptedAt, shipmentCreatedAt, occurredAt và traceId tùy chọn.

Timeline intake hiện tại paidAt <= sellerAcceptedAt <= shipmentCreatedAt <= occurredAt; Created có occurredAt = shipmentCreatedAt. Source phải chốt thứ tự commit khi nhận đơn/tạo vận đơn để đáp ứng thật, hoặc cùng cập nhật hợp đồng có kiểm thử trước khi đổi. Không sửa timestamp gửi đi để che thứ tự thực tế.

Một owner thông báo cho một event. Ghi/giữ snapshot cùng thay đổi nguồn; gửi sau commit và retry cùng event/version/payload. Late event không làm lùi stream. Không đưa shipper phone/POD/address vào thông báo cá nhân nếu contract không cho phép.

## Commerce — ưu tiên cao

order-payments đã có caller durable cho đơn thường và đã được kiểm chứng FE trên bản mới. Caller phải gắn khóa Notification backend từ cấu hình server, không dùng token người mua để phát sự kiện.

Những nhóm cần đối chiếu/bổ sung: order-decisions, order-buyer-cancellations, order-payment-timeouts, order-refunds, order-completions, order-settlements và shipment context phối hợp Fulfillment.

- Completion: source event ổn định; order/shop/shipment IDs, Buyer/Seller IDs, kind/auction ID, orderNo, paid/delivered/completed timestamps thật. Source xác nhận trạng thái Completed và Delivered; không thay ShipmentId bằng OrderId.
- Refund: refundId, WalletCreditTransactionId thật, Buyer ID, đúng order kind/auction ID/orderNo/amount/currency, requested/wallet-credited/completed timestamps và trạng thái Completed/BuyerWallet. Lỗi hoàn tiền không tạo thông báo đã hoàn.
- Settlement: settlementId và wallet credit transaction ID thật, Seller ID/order kind/orderNo/net amount, OrderCompletedAt/WalletCreditedAt/SettledAt, Completed và SellerWallet. Không suy luận transaction ID từ order ID hoặc từ HTTP bool.
- Decision/cancel/timeout: theo DTO của từng endpoint, dựa vào trạng thái đã commit, không gửi lặp từ cả source worker và một nguồn khác.

Source phải giữ bản yêu cầu đúng cho retry/đối soát; Notification chịu trách nhiệm phân loại Email/InApp, không thực hiện giao dịch đơn/ví thay nguồn.

## User

Caller OTP/password/Seller/account status đã có. Khóa outbound phải khớp Notification. Ban/unban và Seller rejection phải có lý do theo DTO; business commit không rollback chỉ vì gửi mail lỗi.

API lookup mới GET /api/v1/users/{userId}/notification-contacts: chỉ backend có X-Notification-Contact-Key hợp lệ, >=32 bytes. Trả DTO {userId,email} cho email đã verified và user không bị xóa, vẫn cho account Banned nhận cảnh báo bảo mật. Không dùng endpoint public profile để lấy PII và không đưa key lên FE.

## Wallet / Catalog / Auction

- Wallet withdrawal-events đã có receiver và FE duyệt/từ chối được kiểm chứng email + in-app. Wallet dùng scoped key WalletNotificationAuth:ServiceKey; không dùng JWT Auction. Chưa có Created/new withdrawal tới Staff.
- Cọc: mặc định Auction là owner lifecycle, không bật Wallet SendAuctionDepositEvents để tạo thông báo trùng. Nếu chuyển owner phải chốt event và payload/auth cùng nhau.
- Catalog gọi in-app với product/report IDs và source event ổn định; loại quan trọng được Notification thêm email, không gửi thêm generic email từ Catalog. Background source cần backend key, không dựa vào ambient role.
- Auction giữ JWT service riêng/scope auction.notifications.write và stable event IDs. Không đổi sang khóa Wallet để bypass. Quan trọng được thêm email qua User lookup; outbid/start/watchlist/report thường chỉ in-app. Cần chạy lại từng ca FE và email Inbox thật, không dùng polling để kết luận Push.

## Nghiệm thu mỗi caller

- [ ] FE thao tác thành công và nghiệp vụ đã commit.
- [ ] Notification receipt đúng source ID, đúng recipients, đúng kênh.
- [ ] Replay cùng event không thêm notice/email; xung đột trả 409.
- [ ] Notification gián đoạn rồi phục hồi không mất sự kiện đã commit.
- [ ] Email Sent đối chiếu hộp thư; Pending không được coi thành công.
- [ ] Đọc/unread badge bền vững và không lộ dữ liệu khi đổi tài khoản.
- [ ] Schema mới nếu cần được thử/backup riêng, không auto migration lên Neon chung.
