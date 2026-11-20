# Báo cáo tích hợp Notification: Buy Now và Giá sàn

Ngày: 20/11/2026.

## Phiên bản và thay đổi

Đã fetch/pull Auction `origin/dev` có Buy Now/Giá sàn tại `0f76028`, giữ thay đổi local bằng backup/stash rồi khôi phục thành công. HEAD local merge `1b00cf1`, vẫn còn các commit và thay đổi local chưa push. Không khẳng định code tích hợp mới đã có trên GitHub.

Thêm hai endpoint in-app `auction-reserve-approval-needed`, `auction-reserve-restriction`; thêm JWT allowlist tương ứng ở Notification và Gateway. Caller Auction phát yêu cầu duyệt khi settlement vào AwaitingSellerApproval, phát cảnh báo/hạn chế khi từ chối hoặc hết hạn. Event được đưa vào cơ chế lưu/gửi hiện có cùng lần lưu nghiệp vụ, dispatcher gửi sau commit.

Sửa caller Buy Now: trường winningBidId trước đây dùng nhầm result.Id, nay dùng buyNowBid.Id thật. Giữ các route winner, order-payment và deposit hiện có.

## Kết quả

| Kiểm chứng | Kết quả |
|---|---|
| Build Release Auction | PASS, 0 lỗi, 5 cảnh báo nullable hiện có |
| Build Release Notification | PASS, 0 lỗi, 0 cảnh báo |
| Build Release Gateway | PASS, 0 lỗi, 0 cảnh báo |
| Toàn bộ Notification.Tests với PostgreSQL riêng | 807 PASS, 0 FAIL, 0 SKIP |
| Gateway: IdentityPropagation, ArchitecturalContractInvariant, RouteCatalogConfiguration | 58 PASS, 0 FAIL, 0 SKIP |
| Phiên dưới giá sàn kết thúc → yêu cầu Seller duyệt | PASS qua caller/dispatcher/Gateway thật |
| Từ chối lần hai → một cảnh báo | PASS |
| Hết hạn → chuỗi lên ba, hạn chế 14 ngày | PASS |
| Seller chấp nhận → reset chuỗi, mở hạn thanh toán, Buyer nhận winner | PASS |
| Từ chối trực tiếp lần ba → thông báo hạn chế | PASS |
| Buy Now → Bid ID thật, hạn thanh toán 24 giờ, replay không tạo trùng | PASS |
| Seller đọc thông báo mới qua Gateway bằng JWT người dùng | PASS |

Các test API mới kiểm tra 401/403, snapshot không hợp lệ, replay có đổi múi giờ/trace, 409 khi đổi dữ liệu cùng SourceEventId và nội dung lưu riêng theo Seller. Cơ chế chống replay đồng thời được kiểm chứng trong nhóm AuctionActivity. Không thêm migration Notification.

## Phương pháp và giới hạn

Luồng nghiệp vụ chạy HTTP từ Auction API và worker thật → Gateway thật → Notification API thật → PostgreSQL riêng → inbox qua Gateway. DB thử được tạo mới trong container và xóa sau test. Wallet/Catalog là HTTP giả lập; User check bypass chỉ trong môi trường thử. Worker email/push tắt.

Auction chưa có test project riêng; ca nguồn được kiểm chứng bằng script tích hợp và trạng thái DB. Các cảnh báo Deposit/User trong log phù hợp với fixture cọc không có hold thật và cấu hình bypass; không chứng minh hoàn tiền/capture thật đã hoàn tất.

Không sửa schema/dữ liệu Neon, không thay cấu hình SMTP/Firebase, không phát thư thật trong đợt kiểm chứng này. Chưa nghiệm thu FE hoặc chuyển tiền Commerce/Wallet thật. Docker đang chạy của nhóm chưa được thay image để tránh tự áp dụng migration mới lên DB dùng chung.

## Bàn giao và việc tiếp theo

1. Review ba phần code Auction, Notification và Gateway cùng contract mới.
2. Đối chiếu/backup DB Auction trước migration Buy Now/Giá sàn có trong dev mới; dữ liệu Notification không cần migration mới.
3. Build lại ba image với cấu hình riêng hiện có và cùng khóa JWT service. Giữ key ring OTP bền vững.
4. Test Seller/Buyer trên FE và thanh toán đơn/phí vận chuyển bằng Commerce/Wallet thật; đối chiếu thông báo với trạng thái nguồn.
5. Email/Push cho hai sự kiện mới làm sau theo phạm vi đã thống nhất.
