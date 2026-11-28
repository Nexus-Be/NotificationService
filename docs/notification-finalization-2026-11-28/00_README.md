# Hoàn thiện Notification Nexus — báo cáo và yêu cầu tích hợp

Ngày đối chiếu: 28/11/2026. Phạm vi: đọc `origin/dev` đã fetch của FE, Gateway, các service; giữ nguyên mọi sửa đổi code local. Các file này là **yêu cầu bàn giao**, không chứng nhận chức năng đã chạy trên server.

## Ưu tiên nghiệm thu

| Mức | Việc cần chốt | Chủ sở hữu | Điều kiện đạt |
|---|---|---|---|
| P0 | Đồng bộ caller/callee đang lệch: Wallet top-up, Catalog in-app, Commerce xác nhận đơn, Fulfillment giao hàng | Các service tương ứng + Notification | Hợp đồng REST và test tích hợp qua Gateway/đường nội bộ đúng, không 404/401, mỗi event chỉ một thông báo logic |
| P0 | Cảnh báo Buyer bị vượt giá theo thời gian thực | Auction, Notification, Gateway, FE | Khi Buyer ở trang khác vẫn nhận cảnh báo tức thời; mất kết nối rồi vào lại vẫn thấy thông báo bền vững |
| P0 | Email giao dịch ổn định trên server | Notification + vận hành | Provider chấp nhận, delivery được ghi nhận, hộp thư thật nhận, retry không gửi trùng; đo hạn mức/quota |
| P0 | DB server dùng chung hạ tầng, riêng quyền sở hữu từng service | Vận hành + từng service | Kết nối đúng DB, migration an toàn, có backup, worker OTP chỉ xử lý payload cùng key ring |
| P1 | Chuẩn hóa chính sách email/in-app, nội dung và deep link | Notification + FE | Đúng người, đúng thời điểm, copy rõ ràng, liên kết mở đúng trang |
| P1 | Báo cáo độ trễ, lỗi delivery, đối soát outbox | Notification + Log | Có trace từ event nguồn tới delivery, có cảnh báo hàng đợi kẹt |

## Chính sách kênh đề nghị

| Sự kiện | In-app | Email | Realtime tới web |
|---|---:|---:|---:|
| OTP đăng ký/đặt lại mật khẩu | Không | Bắt buộc | Không |
| Ban/unban; duyệt/từ chối Seller; đổi/reset mật khẩu | Có đối với sự kiện có phiên sử dụng | Bắt buộc | Có nếu đang online |
| Thanh toán đơn/nạp ví/rút ví/hoàn tiền thành công hoặc thất bại cuối cùng | Có | Có cho thay đổi tiền quan trọng | Có nếu đang online |
| Đơn được Seller xác nhận/từ chối; giao hàng thất bại/hoàn tất | Có | Có khi ảnh hưởng quyền lợi hoặc cần hành động | Có nếu đang online |
| Bị vượt giá, thắng phiên, cần thanh toán/hết hạn, phiên bị hủy | Có | Email cho thắng, thanh toán/hết hạn, hủy; vượt giá thường chỉ in-app | Bắt buộc cho vượt giá nếu đang online |
| Có giá mới ở phiên theo dõi, cập nhật vận chuyển thông thường, đánh giá/bình luận | Có | Không mặc định | Tùy ưu tiên |

Không đồng nhất `HTTP 202`, trạng thái `Pending` hay provider accepted với thư tới hộp thư. Push trình duyệt ngoài nền là một kênh khác; chưa được xem là đạt nếu Firebase/Web Push chưa cấu hình và kiểm chứng thiết bị thật.

## File bàn giao

- [01_DEV_SYNC.md](01_DEV_SYNC.md): trạng thái đồng bộ Git và các conflict giữ nguyên.
- [02_NOTIFICATION.md](02_NOTIFICATION.md): việc trong Notification, DB, mail, API, template.
- [03_AUCTION.md](03_AUCTION.md), [04_COMMERCE.md](04_COMMERCE.md), [05_FULFILLMENT.md](05_FULFILLMENT.md), [06_CATALOG.md](06_CATALOG.md), [07_USER.md](07_USER.md), [08_WALLET.md](08_WALLET.md): yêu cầu từng service.
- [09_GATEWAY.md](09_GATEWAY.md), [10_LOG.md](10_LOG.md), [11_FE.md](11_FE.md): tuyến API, quan sát và giao diện.
- [12_SERVER_DB_EMAIL.md](12_SERVER_DB_EMAIL.md): phương án chạy server/DB/email, các cổng nghiệm thu.
- [13_SERVER_TEST_2026-11-28.md](13_SERVER_TEST_2026-11-28.md): kết quả smoke test trực tiếp trên Render và các ca còn bị chặn.

## Quy tắc tích hợp

Một service sở hữu và ghi DB của mình. Các service trao đổi dữ liệu bằng REST typed DTO; Notification không đọc trực tiếp DB Auction/Commerce/User. Caller chỉ phát sự kiện sau commit, giữ `sourceEventId` ổn định, lưu lỗi và retry an toàn. Notification tạo bản ghi in-app bền vững trước khi phát realtime tới FE. FE lấy lại danh sách sau reconnect và không tự coi cảnh báo realtime là đã đọc.
