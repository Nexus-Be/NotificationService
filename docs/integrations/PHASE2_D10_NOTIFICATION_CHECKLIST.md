# Checklist sửa Notification theo báo cáo E2E Phase 2

Ngày: 20/11/2026. Phạm vi: NotificationService, D-10a/D-10b. Báo cáo kiểm thử gốc dùng a767334; dev trước đợt sửa này là 4e359a7. Không dùng toàn bộ số liệu 296 ca trong báo cáo để kết luận bản sửa hiện tại đã chạy lại full stack.

## Kế hoạch và đối chiếu

- [x] Đọc báo cáo và phân biệt lỗi receiver với lỗi caller/triển khai.
- [x] Đọc DTO và HTTP client Wallet dev 5355f1a, Catalog dev 7a312bc qua snapshot nguồn; không sửa checkout hai service đó.
- [x] Xác nhận Buy Now/Giá sàn đã có contract trên dev Notification, không tạo luồng winner thứ hai.
- [x] Ghi plan trước khi sửa; phân tích GitNexus và kiểm tra thủ công các đường DI/auth/parser/preference do chỉ mục cũ.

## D-10a — Wallet

- [x] Thêm POST withdrawal-events đúng SourceEventId chuỗi và DTO nguồn.
- [x] Nhận Approved/Rejected/Completed/Failed cho đúng recipient.
- [x] Giữ một POST auction-deposits, chọn schema theo danh tính đã xác thực.
- [x] Giữ nguyên JWT/DTO/response Auction hiện có.
- [x] Nhận Wallet Held/Released/Captured/Forfeited/Refunded, đúng bidder/amount/currency.
- [x] Thêm Wallet key scheme phạm vi hẹp, fail closed khi thiếu/sai; không bỏ chống giả mạo.
- [x] Transaction notification/delivery/audit; replay đồng thời, conflict409, receipt ẩn/xóa mềm.
- [x] Swagger mô tả hai schema cọc và credential Wallet.

## D-10b — Catalog

- [x] Thêm canonical ProductBannedReport26, ProductReportResolved27, ProductReportDismissed28.
- [x] Chỉ nhận alias PRODUCT_BANNED_REPORT/REPORT_RESOLVED/REPORT_DISMISSED tại ingress tạo thông báo.
- [x] Thêm POST public in-app theo danh tính service/admin hiện có; không mở anonymous hoặc thêm alias internal.
- [x] Render nội dung trung tính, không xác nhận trừ 10 hay 5 điểm khi chưa có kết quả thực tế.
- [x] Không đưa penaltyPoints/ghi chú quản trị vào payload người nhận.
- [x] Replay theo event ID hoặc report/type/recipient khi caller cũ thiếu event ID.

## Kiểm chứng

- [x] Build Release 0 lỗi, 0 cảnh báo.
- [x] Nhóm test mới và AuctionActivity trước bổ sung ca rollback: 25 PASS/0 FAIL/0 SKIP.
- [x] Toàn bộ Notification.Tests sau bổ sung ca khóa chưa cấu hình và rollback audit: **828 PASS, 0 FAIL, 0 SKIP**.
- [x] Chỉ dùng PostgreSQL Docker riêng, không migration/reset/seed Neon.
- [x] Viết spec và hai contract, rà liên kết nội bộ.

## Còn cần phối hợp/triển khai

- [x] Đồng bộ Wallet key riêng trong .env Wallet/Notification và JSON Notification; có backup riêng, không đưa lên Git.
- [ ] Build lại image và kiểm chứng stack đang chạy trước khi nghiệm thu DB dùng chung.
- [x] Probe dùng nghiệp vụ/client Wallet thật trên PostgreSQL riêng, không còn405/401; đúng người và replay ổn định. Chưa nghiệm thu runtime/FE.
- [ ] Chốt một bên phát mỗi biến động cọc; event ID khác nhau giữa Wallet/Auction chưa được chống trùng xuyên nguồn.
- [x] Catalog local đã chuyển public URL, giữ DI handler truyền danh tính, chuẩn hóa type/ID và kiểm chứng nghiệp vụ Staff qua HTTP.
- [ ] Catalog/User đối chiếu điểm phạt thật; Catalog xử lý D-13 để không cấm lệch trạng thái phiên.
- [ ] Test FE và full stack báo cáo gốc; không đánh dấu PASS bằng POST receiver thủ công.
- [ ] Email/Push cho các luồng mới thực hiện sau.

## Tài liệu

- [Spec](../../specs/06_PHASE2_SERVICE_EVENT_INTAKE.md)
- [Contract Wallet](../api/WalletEvents/WALLET_NOTIFICATION_CONTRACT.md)
- [Contract Catalog](../api/ProductReportEvents/PRODUCT_REPORT_NOTIFICATION_CONTRACT.md)

Cập nhật sau pull Wallet/Catalog: xem [báo cáo caller](WALLET_CATALOG_CALLER_VERIFICATION_20261120.md), Notification832PASS/Catalog50PASS, Wallet547PASS và4test ngoài caller còn lỗi.
