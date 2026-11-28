# Notification Render Free — tiến độ phát hành và nghiệm thu

Ngày kiểm tra: 28/11/2026. Bản ứng viên: `codex/notification-render-free-demo-20261128` trên `NotificationService`.

## Đã kiểm chứng trước phát hành

- Đã ghép `origin/dev` với API `POST /api/v1/notifications/wallet-topups` và giải xung đột enum/test. Không thay đổi code của service khác.
- Build Release và Docker image thành công. Bộ test đầy đủ đạt **916/916**, không skip, trên PostgreSQL fixture được khôi phục từ backup Neon Notification.
- Backup riêng tư: `.local-backups/server-20261128/notification-neon-20261128.dump`; `pg_restore --list` đọc được 250 mục. Fixture có 37 bảng; `dotnet ef database update` báo không có migration mới. **Không chạy migration trên Neon**.
- Image Production chạy với fixture, auto-migration và worker tắt: `/health/ready` trả 200, DB Healthy; Swagger có route `wallet-topups`; chuông và hub ẩn danh trả 401, header danh tính giả trả 403, Wallet không có key trả 401.
- Resend HTTPS sender có test cho provider ID, idempotency key ổn định khi retry, lỗi tạm thời/vĩnh viễn. Hub yêu cầu User JWT và outbid chỉ phát tín hiệu sau khi bản ghi in-app đã lưu, không phát lại khi replay.
- Bản Render đang chạy: `/health/ready` 200, Swagger có 80 paths **chưa có** `wallet-topups`. Do đó chưa thể xem bản ứng viên là đã deploy.

## Cổng phát hành trên Render

1. Ghim đúng commit ứng viên trên Render, dùng Dockerfile cổng 8007. Workflow GitHub hiện chỉ tự deploy khi push `dev`; push nhánh ứng viên riêng không cập nhật Render.
2. Kiểm tra Environment bằng **tên biến, không xuất giá trị**: `ConnectionStrings__DefaultConnection` trỏ đúng Notification DB; `DatabaseInitialization__ApplyMigrationsOnStartup=false`; JWT issuer/audience/signature khớp UserService; key Auction/Wallet/internal được cấp riêng; `OtpProtection__RequireSharedKeyRing=true` và `OtpProtection__SharedKeyRingBase64` là cùng key ring của worker dùng DB này.
3. Resend: `EmailProvider=Resend`, `RESEND_API_KEY`, `RESEND_FROM_EMAIL`, `RESEND_FROM_NAME`. Trước khi bật worker, xác nhận sender/recipient được Resend cho phép; chưa có domain xác minh thì chỉ kiểm tra người nhận thử mà tài khoản Resend cho phép.
4. Rà các delivery/OTP Pending, Failed và thời hạn trước khi bật `OtpWorker__Enabled=true` và `DeliveryWorker__Enabled=true`. Không cho hai phiên bản worker với key ring khác nhau xử lý cùng DB. Render Free có thể ngủ khi không có lưu lượng nên queue có thể chậm.
5. Sau deploy kiểm tra `/health/ready`, Swagger `wallet-topups`, anonymous/forged/JWT Buyer và trạng thái từng worker. Nếu lỗi, tắt worker và rollback image/commit trước; giữ nguyên dữ liệu Neon.

## Ca nghiệm thu cần chạy trên bản Render mới

| Ca | Bằng chứng bắt buộc | Trạng thái |
|---|---|---|
| OTP đăng ký, resend, reset | FE request, OTP dispatch, provider message ID, inbox thật, mã hết hạn | Chưa chạy trên bản mới |
| In-app từ caller thật | `sourceEventId`, đúng Buyer, badge, read/unread sau reload | Chưa chạy trên bản mới |
| Ví nạp tiền | Caller Wallet, một event một notification, email nếu chính sách yêu cầu | Chờ Wallet caller/server |
| Đấu giá vượt giá online | Auction bid commit → in-app → đúng Buyer nhận `NotificationCreated`; đo độ trễ | Chờ Gateway WebSocket và FE |
| Đấu giá mất kết nối | Nhiều bid khi offline → FE reconnect lấy đủ bản ghi từ REST, không toast trùng | Chờ Gateway WebSocket và FE |

Gateway và FE cần triển khai theo [`NOTIFICATION_REALTIME_HANDOFF.md`](../integrations/NOTIFICATION_REALTIME_HANDOFF.md); Wallet theo [`WALLET_TOPUP_NOTIFICATION_HANDOFF.md`](../integrations/WALLET_TOPUP_NOTIFICATION_HANDOFF.md). Notification không tự sửa code các repo đó. Khi FE/Gateway/Auction vẫn ở local, chỉ ghi **local → Notification Render**, không ghi full flow server PASS.

## Chặn hiện tại

- Chưa có quyền truy cập dashboard/API Render trong môi trường tác vụ để chọn commit, xem tên Environment và thực hiện deploy; API key Resend đã được người dùng báo là lưu trên Render nhưng chưa xác minh `RESEND_FROM_EMAIL` và quyền gửi.
- Chưa có URL Gateway/FE/Auction trên server và tài khoản Buyer/Seller thử cho môi trường đó; chưa thể kết luận realtime và email toàn luồng đạt.
- Không có domain gửi đã xác minh; email người nhận rộng vẫn bị chặn theo chính sách Resend.
