# Kiểm chứng caller Wallet/Catalog sau pull dev — 20/11/2026

## Code đã lấy về và kết luận caller

- Wallet dev 5355f1a: đã có NotificationClient cho withdrawal-events và auction-deposits, gọi sau commit và ghi audit khi gửi lỗi. Không cần viết lại caller.
- Catalog dev 7a312bc: đã có caller nhưng URL internal cũ và type kiểm duyệt chưa khớp. DI vốn đã có IdentityPropagationHandler và InternalServiceAuthHandler; chỉ đọc file client sẽ không thấy danh tính/key này.
- Đã giữ sửa đổi local Wallet bằng backup branch/stash rồi khôi phục; Catalog merge dev giữ commit local. Không force/reset lịch sử.

## Phần triển khai tiếp

Catalog local đã chuyển POST sang public in-app, chuẩn hóa7type, gửi event ID, giữ handler chain. Report có ID ổn định cho cùng report/type/recipient; kiểm duyệt thông thường có ID mới cho mỗi lần phát để không gộp nhầm chu kỳ duyệt lại. Body được giữ trong lần gửi; durable retry từ audit cho kiểm duyệt thông thường vẫn chưa có.

Các thông báo kiểm duyệt bỏ câu trừ điểm cố định. JSON lý do REJECT/SUSPEND dùng serializer nên dấu nháy/xuống dòng không làm hỏng payload. Logic phạt User không thay trong task này.

Notification bổ sung ProductApproved29, ProductRejected30, ProductSuspended31, ProductBanned32; role Staff được nhận tại public in-app cùng service/admin theo ngữ cảnh backend đã xác thực. Contract Wallet và Auction giữ nguyên. Không thêm migration.

Khóa Wallet→Notification đã được đồng bộ trong cấu hình riêng: Wallet .env; Notification .env và hai appsettings riêng. Các file có backup riêng, bị Git bỏ qua; khóa không có trong source/doc. Wallet không có hai JSON riêng tại đường dẫn chuẩn nên không tạo file JSON mẫu thay cấu hình thật. Docker Wallet đọc key qua Compose từ .env.

## Kết quả test

| Kiểm chứng | Kết quả |
|---|---|
| Catalog toàn bộ | **50 PASS, 0 FAIL, 0 SKIP** |
| Notification toàn bộ | **832 PASS, 0 FAIL, 0 SKIP** |
| Wallet toàn bộ với PostgreSQL riêng | **547 PASS, 4 FAIL, 0 SKIP**, tổng551 |
| Bốn Wallet NotificationIntegrationTests | **4 PASS** |
| Probe client/nghiệp vụ nguồn thật → HTTP Notification → PostgreSQL riêng | **18 kiểm tra PASS** |

Lượt Wallet đầu chưa cấu hình DB riêng là507PASS/42SKIP; đã chạy bổ sung DB riêng, không dùng kết quả bỏ qua để kết luận đạt.

Probe chạy Wallet qua MediatR/validation thật, xử lý Approved/Failed, giữ cọc/replay/nhả cọc; Notification lưu đúng recipient và không có audit dispatch lỗi. Catalog chạy ModerateReport bằng Staff đến thông báo Seller/reporter và replay; bốn trạng thái kiểm duyệt được tiếp nhận; REJECT/SUSPEND chạy ProductService thật với lý do chứa dấu nháy/xuống dòng. Guest không phát được thông báo.

DB Notification, Wallet và Catalog trong probe đều là PostgreSQL riêng, migration từ DB trống. User penalty/ảnh/kho trong probe được thay thế; không chứng minh điểm phạt, giao dịch provider hoặc FE thật đã nghiệm thu. Không dùng Neon, không reset/seed DB chung.

## Bốn test Wallet còn lỗi, ngoài caller Notification

| Test | Lỗi quan sát |
|---|---|
| P0PostgresRegressionTests.ListTransactions_WithNonUtcDateOffsets_SucceedsOnPostgres | Fixture có1000 nhưng gọi thanh toán50000, InsufficientBalanceException |
| AuctionHoldWithdrawalRaceTests.ConcurrentHoldAndWithdrawal_CannotOverreserve | Key wd:... chứa dấu hai chấm, không đạt validator rút tiền hiện tại |
| AuctionOrderEscrowPostgresTests.FailedCancelInsert_RollsBackBalanceEscrowTransactionAndAudit | Đếm audit reconciliation toàn DB kỳ vọng0 nhưng có1; cần kiểm tra cô lập fixture |
| StripeSettlementPostgresTests.DirectCreation_CommitsBeforeProviderCallAndBlocksConcurrentRetry | So sánh toàn bộ record trước/sau PostgreSQL khác nhau ở timestamp; cần đối chiếu precision |

Các ca này không dùng NotificationClient trong đường lỗi. Không thay code tài chính hoặc nới assertion để làm xanh trong task caller này. Cần xử lý riêng trước khi kết luận Wallet toàn bộ PASS.

## Rollout và phần còn lại

- [x] Pull hai service và giữ code/config local.
- [x] Sửa caller Catalog và đối chiếu receiver, thông báo/replay đúng người.
- [x] Đồng bộ Wallet key riêng, giữ khỏi Git.
- [x] Test source HTTP và các bộ test liên quan, ghi rõ4lỗi Wallet còn lại.
- [ ] Build lại/recreate image runtime sau khi preflight migration Catalog/Wallet trên DB dùng chung và backup; task này chưa thay stack đang chạy.
- [ ] Chốt một bên phát mỗi biến động cọc giữa Wallet/Auction để không có hai event khác ID cho cùng giao dịch.
- [ ] Xử lý4test Wallet, durable retry Catalog, D-13 và đối chiếu điểm thực tế ở nhóm nguồn.
- [ ] Test FE, Email/Push/provider và DB dùng chung.

Code mới và cấu hình local chưa tự xuất hiện trên GitHub hoặc trong container đang chạy. Chưa commit/push trong task này.

## Tài liệu

- [Spec](../../specs/06_PHASE2_SERVICE_EVENT_INTAKE.md)
- [Contract Wallet](../api/WalletEvents/WALLET_NOTIFICATION_CONTRACT.md)
- [Contract Catalog](../api/ProductReportEvents/PRODUCT_REPORT_NOTIFICATION_CONTRACT.md)
