# Yêu cầu cho WalletService

## Đã có trên `origin/dev`

Caller top-up gửi `POST /api/v1/notifications/wallet-topups` với `SourceEventId = wallet-topup:{topupId}:succeeded`, `TopupId`, `TransactionId`, `RecipientUserId`, `Amount`, `Currency`, `ConfirmedAt`, `TraceId`; audit được lưu cùng settlement. Đây là hướng đúng. Notification `origin/dev` chưa có endpoint tương ứng; checkout Notification local có phần chưa ghép.

## Cần phối hợp

1. Chốt DTO/status/authorization với Notification sau khi endpoint được merge; thêm test contract thật thay vì chỉ mock HTTP. Kiểm tra số tiền `long` của Wallet với `decimal` phía Notification, định dạng VND và timezone.
2. Không phát top-up khi chỉ tạo Stripe Checkout; chỉ phát khi webhook đã xác thực và số dư đã commit. Webhook lặp cùng `topupId` phải giữ một event/notification. Không đưa chi tiết thẻ vào payload.
3. Bổ sung hoặc đối chiếu các sự kiện rút ví, hoàn tiền, cộng tiền Seller/auction settlement theo đúng owner của giao dịch; mỗi thay đổi tiền quan trọng có in-app và email theo chính sách, nhưng không phát hai lần từ Wallet và Commerce.
4. Theo dõi hàng audit/outbox chưa dispatch, retry khi Notification tắt và 409 payload khác. Thử với DB fixture trước khi migration DB server.

**PASS:** Stripe test payment thành công, DB Wallet có giao dịch/số dư đúng, một notification đúng Buyer, thư thật nếu policy yêu cầu; fail/cancel không gửi thành công.
