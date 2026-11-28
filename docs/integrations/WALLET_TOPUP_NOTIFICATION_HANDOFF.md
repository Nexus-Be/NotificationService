# Wallet → Notification: thông báo nạp ví Stripe thành công

## Phạm vi và trạng thái

NotificationService cung cấp API nhận kết quả nạp ví và lưu **một thông báo in-app cùng một email** cho chủ ví. WalletService cần bổ sung caller và cơ chế gửi lại; không có thay đổi code Wallet trong phần bàn giao này. API trả 200 khi Notification đã lưu thông báo/delivery, **không xác nhận SMTP đã phát thư**.

## REST contract

- **Method/route:** `POST /api/v1/notifications/wallet-topups` tại NotificationService (cổng 8007 trong Docker).
- **Caller duy nhất:** WalletService. Gửi `X-Internal-Service-Key: <NOTIFICATION_SERVICE_KEY>`; khóa phải khớp `WalletNotificationAuth:ServiceKey` của Notification, dài tối thiểu 32 byte. Không đi qua trình duyệt/FE và không đính kèm `Authorization` hay các header `x-user-*`.
- **Content-Type:** `application/json`. JSON không cho phép trường lạ.
- **Thời điểm:** chỉ sau khi Stripe được xác minh và Wallet đã commit `TopupStatus.Success`, số dư và một `WalletTransaction` loại `TopupStripe` thành công.

```json
{
  "sourceEventId": "wallet-topup:11111111-2222-3333-4444-555555555555:succeeded",
  "topupId": "11111111-2222-3333-4444-555555555555",
  "transactionId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "recipientUserId": "99999999-8888-7777-6666-555555555555",
  "amount": 10000000,
  "currency": "VND",
  "confirmedAt": "2026-11-27T10:00:00Z",
  "traceId": "optional-trace"
}
```

| Trường | Quy tắc |
|---|---|
| `sourceEventId` | Chính xác `wallet-topup:{topupId:D}:succeeded` với UUID dạng chữ thường. Giữ nguyên khi retry; không dùng Stripe webhook event ID vì Stripe có thể phát lại/báo cùng giao dịch qua nhiều sự kiện. |
| `topupId` | ID `WalletTopup`, cố định cho một lần nạp. |
| `transactionId` | ID bản ghi `WalletTransaction` đã cộng ví, không phải Checkout Session hoặc PaymentIntent ID. |
| `recipientUserId` | Chủ ví thực tế trong Wallet. Notification lấy email hiện hành từ UserService theo ID này lúc gửi; Wallet không truyền email. |
| `amount` / `currency` | Số nguyên 10.000–99.999.999 VND, đúng khoản đã cộng. |
| `confirmedAt` | UTC, thời điểm chốt khoản nạp; không quá 30 giây trong tương lai. Giữ nguyên khi retry. |
| `traceId` | Tùy chọn, tối đa 100 ký tự; có thể đổi khi retry. |

**200 OK**, ví dụ:

```json
{
  "sourceEventId": "wallet-topup:11111111-2222-3333-4444-555555555555:succeeded",
  "notificationId": "22222222-3333-4444-5555-666666666666",
  "isReplay": false,
  "deliveries": [
    { "deliveryId": "33333333-4444-5555-6666-777777777777", "channel": "Email", "status": "Pending" },
    { "deliveryId": "44444444-5555-6666-7777-888888888888", "channel": "InApp", "status": "Sent" }
  ]
}
```

Lặp cùng `topupId` và payload nghiệp vụ trả 200 với `isReplay: true`, giữ nguyên notification/delivery. `traceId` và offset biểu diễn cùng thời điểm không làm thay đổi nội dung nghiệp vụ. Dùng cùng `topupId` với số tiền, transaction, người nhận hoặc thời điểm khác trả **409 `WALLET_TOPUP_CONFLICT`**. Dữ liệu sai trả **400 `VAL_INVALID_INPUT`**; thiếu/sai service key trả **401**. Thiếu cấu hình channel trả **409 `CHANNEL_NOT_FOUND`**.

## Việc WalletService cần làm

1. Khi `StripeSettlement` chuyển `Topup` sang `Settled`, lưu bản ghi yêu cầu gửi thông báo **trong cùng giao dịch DB** với khoản cộng ví. Chỉ ghi một bản theo `topupId`; không ghi khi `AlreadySettled`, `Pending`, `DirectOrder`, thất bại hoặc `ReconciliationRequired`.
2. Sau commit, worker/caller REST gửi payload nêu trên. Khi Notification tạm ngừng, giữ yêu cầu và retry cùng nội dung với backoff. Lỗi thông báo không hoàn tác tiền. Nếu trả 409, ghi lỗi cần đối soát; không tự đổi `sourceEventId` để vượt chống trùng.
3. Cấu hình base URL Notification, service key và timeout. Không ghi key hoặc payload chứa thông tin nhạy cảm vào log.
4. Chạy test: một webhook Stripe thành công tạo một giao dịch ví và một yêu cầu Notification; webhook lặp/reconcile lặp không cộng hoặc gửi thêm; Notification tạm dừng rồi phục hồi vẫn nhận đúng một sự kiện.

## Cách Notification xử lý

- Một thông báo thuộc `recipientUserId`; in-app có thể hiện ngay trong quả chuông.
- Email dùng địa chỉ đã xác minh từ API `notification-contacts` của UserService và gửi qua delivery worker. Subject: **Nạp tiền vào ví thành công**; nội dung có số tiền, thời gian và mã giao dịch.
- Email chưa tới khi `Pending`; đối chiếu `Sent` và hộp thư thật. Cần bật worker Email và cấu hình SMTP/User recipient resolver trên môi trường test.
- Không gửi Push trong hợp đồng này. FE dùng API đọc notification hiện có; không gọi route ghi sự kiện.

## Nghiệm thu đầu cuối

Chạy Stripe Sandbox với thẻ test: sau khi Wallet ghi `Success`, đối chiếu số dư, lịch sử giao dịch, đúng một mục quả chuông và một email thực nhận. Tắt Notification rồi bật lại để kiểm tra gửi bù; thử webhook lặp để xác nhận không cộng tiền hoặc gửi trùng. Không dùng DB chung của service khác và không chạy migration/reset trên DB server để test.
