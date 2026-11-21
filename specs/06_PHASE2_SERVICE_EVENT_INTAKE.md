# Spec: Phase 2 nhận sự kiện Wallet và Catalog

Nguồn vấn đề: báo cáo E2E Phase 2 ngày 20/11/2026, lỗi D-10a/D-10b. Baseline báo cáo là Notification a767334; dev 4e359a7 đã có AuctionActivity/Buy Now/Giá sàn. Không tạo lại endpoint Giá sàn hoặc enum BuyNow nếu hợp đồng đã tái sử dụng auction-winners.

## Invariants

1. Wallet xác nhận kết quả tài chính; Notification chỉ lưu sự kiện đã xác nhận, không sửa số dư/đơn/phiên.
2. Đúng một action POST auction-deposits. Danh tính xác thực chọn hợp đồng; không suy caller từ payload. Auction JWT và response cũ giữ nguyên.
3. Wallet key chỉ hoạt động cho POST withdrawal-events/auction-deposits; chưa cấu hình phải fail closed. Không dùng credential tài chính để đọc inbox.
4. SourceEventId chuỗi của Wallet được namespaced/hash thành GUID, chuỗi gốc giữ để đối soát. Không đổi DB hiện có; enum cũ giữ nguyên.
5. Giao dịch ghi notification + InApp delivery + audit nguyên tử; replay kể cả ẩn/xóa mềm không requeue. Đổi snapshot cùng event ID trả 409.
6. Catalog aliases chỉ áp dụng ở ingress tạo thông báo. Type API/DB canonical: ProductBannedReport26, ProductReportResolved27, ProductReportDismissed28. WithdrawalEvent25; ProductApproved29/ProductRejected30/ProductSuspended31/ProductBanned32; Audit ServiceEventAccepted30.
7. Thông báo Catalog không tự xác nhận trừ điểm hoặc lộ ghi chú quản trị. Không sửa D-13/D-14 hay nghiệp vụ tiền/kho thuộc service khác.
8. Email/Push mới để sau. Không bật delivery worker hoặc gửi provider thật để test receiver.

## Cấu trúc

- API: WithdrawalEventsController, AuctionDepositsController, InAppNotificationsController.
- Auth: WalletNotificationAuth, đăng ký cùng bộ xác thực service hiện có.
- Application: WalletEvents (DTO/use cases), ServiceEvents/InAppEventWriter (persist/replay nguyên tử), Notifications/UserNotificationService.ProductReports.cs (canonicalization/recipient-safe content).
- Domain: thêm giá trị enum cuối danh sách; không thay entity/seed/schema.
- Swagger: mô tả hai DTO cọc bằng oneOf; WalletServiceKey cho Wallet, Bearer cho Auction.
- Preferences: type mới thuộc nhóm in-app; chưa mở thêm danh mục Email/Push trong API preferences hiện có.

## Acceptance

| Ca | Điều kiện đạt |
|---|---|
| Withdrawal | Approved/Rejected/Completed/Failed tạo đúng một notice cho recipient |
| Wallet deposit | Held/Released/Captured/Forfeited/Refunded, đúng bidder/tiền/tham chiếu |
| Compatibility | Auction DTO/JWT/replay tiếp tục hoạt động; DTO trộn bị từ chối |
| Auth | thiếu/sai/khóa ngắn/identity giả không ghi dữ liệu; Wallet key không đọc inbox |
| Replay | concurrent/đổi trace/múi giờ không nhân bản; đổi dữ liệu trả409; hidden receipt ổn định |
| Atomicity | audit write lỗi thì notification/delivery rollback; retry sau phục hồi thành công |
| Ownership | guest401, người khác404, chủ tài khoản đọc và cập nhật read state được |
| Catalog | alias và canonical tạo đúng type; không có điểm cố định/ghi chú nội bộ trong nội dung |
| Swagger | một POST cọc, có withdrawal-events/in-app và hai schema cọc |

Chạy build Release và toàn bộ Notification.Tests với NOTIFICATION_TEST_DB trỏ PostgreSQL Docker riêng. Chưa được kết luận full E2E Wallet/Catalog/FE khi caller/auth hoặc ownership phát cọc chưa đồng bộ.

## Contract

- [Wallet](../docs/api/WalletEvents/WALLET_NOTIFICATION_CONTRACT.md)
- [Catalog](../docs/api/ProductReportEvents/PRODUCT_REPORT_NOTIFICATION_CONTRACT.md)
- [Buy Now/Giá sàn giữ nguyên](../docs/api/AuctionReserve/AUCTION_BUY_NOW_RESERVE_CONTRACT.md)

Bổ sung sau pull Catalog: public in-app cho phép service/admin/staff theo danh tính Gateway truyền qua backend. Catalog giữ handler chain hiện có, đổi URL, chuẩn hóa type/event ID và serialize JSON lý do an toàn. Đã kiểm chứng client và nghiệp vụ nguồn thật trên DB riêng; thông báo không phải bằng chứng User đã áp dụng điểm phạt.
