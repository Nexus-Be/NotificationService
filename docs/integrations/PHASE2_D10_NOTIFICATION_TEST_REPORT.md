# Kết quả sửa Notification theo báo cáo E2E Phase 2

Ngày: 20/11/2026. Baseline code trước sửa: dev 4e359a7. Phạm vi chỉ NotificationService; không phải chạy lại toàn bộ stack của báo cáo gốc.

## Đã triển khai

| Lỗi/giao điểm | Bản sửa Notification | Trạng thái |
|---|---|---|
| D-10a: withdrawal-events thiếu route | Thêm receiver đúng DTO Wallet, 4 trạng thái, string SourceEventId, private in-app | PASS tại receiver |
| D-10a: cọc Wallet không khớp Auction | Một POST auction-deposits, chọn DTO bằng Wallet key hoặc JWT Auction; nhận 5 trạng thái Wallet | PASS tại receiver và hồi quy Auction |
| D-10b: type Catalog bị từ chối | Canonical type26–28 và alias legacy ở ingress | PASS tại receiver |
| D-10b: nội dung trừ điểm sai | Render trung tính, bỏ số điểm chưa xác nhận và ghi chú nội bộ khỏi nội dung/payload | PASS tại receiver |
| Retry | Hash snapshot, transaction/advisory lock, receipt hidden/deleted, conflict409 | PASS |
| Khả năng phục hồi | Ghi audit lỗi rollback notification/delivery; retry sau phục hồi | PASS |

## Kiểm thử

- Build Release: **0 lỗi, 0 cảnh báo**.
- Full Notification.Tests: **828 PASS, 0 FAIL, 0 SKIP**, PostgreSQL18 Docker riêng, thời gian 4 phút21 giây.
- So với baseline807: **21 ca mới**. Nhóm mục tiêu trước hai ca bổ sung cuối:25PASS, gồm19ca mới và6ca AuctionActivity.
- Ca HTTP kiểm tra payload Wallet, auth, trạng thái, replay/concurrency/conflict, receipt ẩn, quyền đọc/đánh dấu, alias Catalog và route Swagger.
- Migration chain chỉ chạy trên database thử mới. Container thử được xóa sau kiểm chứng.
- File kết quả trong workspace kiểm chứng: phase2-full.trx và phase2-targeted.trx. Không đưa OTP/key/token/chuỗi kết nối vào tài liệu.

## Giới hạn và bàn giao

Receiver đã sửa không có nghĩa caller đang chạy đã được đồng bộ. Cần đưa đúng Wallet key từ cấu hình riêng vào receiver và gọi trực tiếp trong mạng backend theo contract; key này chưa được thay trong các file riêng của người dùng. Gateway có thể chặn header key, không nới kiểm tra đó trong task này.

Catalog dev còn URL legacy và chưa gửi credential/danh tính; cần nhóm nguồn cập nhật theo contract public in-app hoặc thống nhất đường backend có bảo vệ. Không bỏ bảo vệ endpoint cũ để làm ca test thành công. Số điểm phạt thật và D-13 thuộc Catalog/User.

Wallet/Auction phải chốt một bên phát mỗi biến động cọc; receiver không tự gộp hai event khác ID khi chưa có tham chiếu chung. Chưa kiểm chứng Wallet/Catalog/FE thật, Email/Push mới hoặc toàn bộ296ca của báo cáo gốc. Không sửa DB Neon, không migration live, không commit/push hoặc triển khai runtime trong đợt này.

## Tài liệu

- [Checklist](PHASE2_D10_NOTIFICATION_CHECKLIST.md)
- [Spec](../../specs/06_PHASE2_SERVICE_EVENT_INTAKE.md)
- [Contract Wallet](../api/WalletEvents/WALLET_NOTIFICATION_CONTRACT.md)
- [Contract Catalog](../api/ProductReportEvents/PRODUCT_REPORT_NOTIFICATION_CONTRACT.md)
