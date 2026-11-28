# Bàn giao cho NotificationService

## Đã thấy trên `origin/dev`

REST cho OTP, thay đổi mật khẩu/trạng thái tài khoản, quyết định Seller; thanh toán đơn, các trạng thái đấu giá, vận chuyển, hoàn tiền/rút ví; API quả chuông và tùy chọn kênh. Có worker OTP riêng, delivery email/push, nguồn event và kiểm tra trùng cho nhiều luồng. Chưa xem những API tồn tại là full flow đạt khi caller hoặc server chưa được thử.

## Cần hoàn thiện

1. **P0 — Hợp đồng ví:** `origin/dev` chưa có `POST /api/v1/notifications/wallet-topups`, trong khi Wallet `dev` đã gọi route đó. Checkout Notification local đang có phần triển khai chưa ghép lên `dev`. Đối chiếu DTO `sourceEventId`, `topupId`, `transactionId`, `recipientUserId`, `amount`, `currency`, `confirmedAt`, `traceId`; merge có chọn lọc, kiểm tra duplicate/conflict và test tích hợp. Không thêm alias khác để che route lệch.
2. **P0 — Realtime cá nhân:** sau khi bản ghi in-app commit, phát event tối thiểu `{notificationId, recipientUserId, type, createdAt, unreadCount?}` đến đúng user; nếu publish thất bại phải đối soát/retry hoặc FE bắt kịp qua `GET /notifications/me`. Xác thực room theo JWT; không phát toàn bộ nội dung nhạy cảm vào room đấu giá chung. Quy định thứ tự và idempotency giữa REST, DB và event realtime.
3. **P0 — Email server:** thay đường gửi Gmail SMTP bằng một provider email giao dịch dùng HTTPS API trên server; trừu tượng sender, giữ cùng template/receipt, map lỗi tạm thời và lỗi vĩnh viễn, lưu provider message ID, timeout/retry có idempotency. Cần domain gửi được xác minh SPF/DKIM/DMARC; giữ SMTP như fallback có kiểm soát nếu môi trường cho phép. Không có provider thực sự “không giới hạn”.
4. **P0 — DB/worker:** kiểm tra `__EFMigrationsHistory`, backup và dry-run migration trên bản sao trước khi chạm DB server; không auto-migrate/seed dữ liệu chung. Nếu nhiều replica cùng xử lý OTP, phải dùng cùng Data Protection key ring hoặc để một worker xử lý; kiểm tra lease/claim và retry. Tách API khỏi worker nếu nền tảng web free có thể ngủ.
5. **P1 — Nội dung:** một nguồn template versioned cho email và in-app; tiếng Việt nhất quán, tên sản phẩm/đơn, số tiền và thời điểm được format, nút hành động/deep link hợp lệ, lý do ban/từ chối được escape, tránh đưa OTP/số dư/token vào log. Ghi rõ sự kiện nào email + in-app theo [chính sách](00_README.md).
6. **P1 — Quan sát:** dashboard các trạng thái `Accepted → Pending → Sent/Failed/Suppressed`, tuổi hàng đợi, retry, số duplicate, lỗi provider, đối soát outbox nguồn với notification; admin có cơ chế retry có kiểm soát.

## API/hợp đồng cần chốt

- Caller service phải mang danh tính service, `sourceEventId` ổn định và `traceId`; FE chỉ gọi endpoint đọc/đánh dấu của chính user. Tránh cho trình duyệt gọi endpoint phát thông báo nghiệp vụ.
- Với event tiền/đấu giá, `409` cùng ID nhưng payload khác phải được ghi lỗi hợp đồng; gửi lại cùng ID và payload trả cùng kết quả logic.
- Với vận chuyển, `Delivered` chỉ là bên vận chuyển giao thành công; `OrderCompleted` chỉ sau điều kiện nghiệp vụ Commerce xác nhận.

## Nghiệm thu

Gửi event thật từ caller qua đường mạng server, đối chiếu event ID, một bản ghi in-app, một receipt đúng kênh, thiết bị/web nhận realtime khi online, khôi phục danh sách khi offline. Email bắt buộc phải xác nhận hộp thư thật; `Sent` một mình chưa đủ.
