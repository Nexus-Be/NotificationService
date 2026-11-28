# Yêu cầu cho CommerceService

## Đối chiếu `origin/dev`

Đã thấy outbox `order-payments` sau thanh toán. Chưa thấy caller `order-decisions`, `order-completions`, `order-refunds`, `order-settlements` trong cây `origin/dev`; checkout local hiện có phần `OrderDecisionNotices` chưa ghép. Cần rà lại từng trạng thái thực tế của Commerce trước khi đánh dấu thiếu hoàn toàn.

## Cần triển khai/kiểm chứng phía Commerce

1. Sau commit thanh toán thành công: thông báo Buyer và Seller, đúng `subOrderId`/`orderId`, số tiền và link đơn; thanh toán thất bại không phát sự kiện thành công. Replay webhook không tạo thông báo mới.
2. Seller **xác nhận/từ chối đơn**: merge và test phần outbox local theo DTO `POST /api/v1/notifications/order-decisions`. Buyer nhận đúng kết quả/lý do, Seller chỉ nhận bản ghi cần thiết; không phát trước khi trạng thái đơn commit.
3. Hoàn tất, hủy, hoàn tiền, settlement: định nghĩa owner của event và điểm commit với Wallet/Fulfillment; chỉ phát khi sự kiện tài chính hoặc trạng thái cuối cùng đã được nguồn xác nhận. Có `sourceEventId` ổn định cho từng chuyển trạng thái, không gửi trùng qua nhiều service.
4. Dispatcher: lưu payload/snapshot tối thiểu và trạng thái retry, cảnh báo outbox kẹt, timeout hợp lý, xử lý 409 payload khác như lỗi hợp đồng. Không làm request mua hàng phụ thuộc thời gian gửi mail.

## Kênh

Thanh toán/hoàn tiền/settlement quan trọng: in-app và email theo chính sách chung. Seller xác nhận đơn: in-app ngay; email nếu cần hành động hoặc đây là mốc quan trọng. Cập nhật trạng thái bình thường: in-app. Notification quyết định template/kênh; Commerce chỉ gửi fact đã commit.

**PASS:** đối chiếu trạng thái đơn trong DB Commerce, outbox, receipt Notification, chuông Buyer/Seller và hộp thư cho những event yêu cầu email.
