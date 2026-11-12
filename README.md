# NotificationService

Notification REST API trên .NET8/PostgreSQL, cổng **8007**. Nhận OTP/email nghiệp vụ từ service nguồn, lưu receipt và retry trong DB; phục vụ thông báo in-app, thiết bị và tùy chọn kênh.

## Cách chạy dành cho thành viên

1. Pull nhánh `dev`.
2. Nhận **ba file cấu hình riêng** từ người phụ trách service; không tạo lại giá trị khóa trên từng máy.
3. Đặt file đúng vị trí:

```text
NotificationService/
├── .env
└── Notification.Api/
    ├── appsettings.json
    └── appsettings.Development.json
```

4. Mở Docker Desktop. Các backend tích hợp dùng Docker network `nexus-network`; nếu máy chưa có network này, tạo một lần:

```powershell
docker network create nexus-network
```

Bỏ qua bước tạo nếu network đã tồn tại. Sau đó chạy tại thư mục repo:

```powershell
docker compose up -d --build notification-service
docker compose ps
```

Kiểm tra [readiness](http://localhost:8007/health/ready) và [Swagger](http://localhost:8007/swagger). Readiness phải trả200/Ready; `/health` đơn thuần chỉ chứng minh tiến trình còn hoạt động.

Docker nhận cấu hình từ `.env`; các file JSON riêng được dùng khi chạy trực tiếp bằng .NET/IDE. File JSON bí mật không được đóng gói trong Docker image. Không cần tạo DB local hay sao chép Docker volume khóa.

## Quy tắc khi dùng DB server chung

Tất cả instance Notification xử lý cùng DB phải dùng **cùng bộ khóa** `OTP_SHARED_KEY_RING_BASE64`, cùng code và cùng application/purpose mã hóa. Bản mới nạp bộ khóa riêng từ cấu hình theo chế độ chỉ đọc, không tự sinh khóa khác trên từng máy. Docker yêu cầu cấu hình khóa chung và sẽ dừng sớm nếu thiếu/sai.

**Cập nhật hoặc dừng tất cả bản Notification cũ trước khi test OTP.** Một bản cũ dùng bộ khóa khác vẫn có thể lấy job và làm nó lỗi `OTP_PAYLOAD_UNAVAILABLE`, dù máy của bạn đã cập nhật đúng. Job đã Failed/hết hạn không được đổi lại Queued; yêu cầu mã mới sau khi rollout hoàn tất.

Chi tiết và cách xoay khóa: [SHARED_OTP_KEYS.md](docs/SHARED_OTP_KEYS.md). Base64 không phải mã hóa bảo vệ file cấu hình. Người phụ trách cung cấp bundle còn hạn; không chia sẻ file riêng qua Git. Mật khẩu từng xuất hiện trong lịch sử Git cần được thu hồi/đổi riêng.

## Tích hợp service nguồn và FE

Các service trong cùng network gọi `http://nexus-notification-service:8007`. Gateway/FE tiếp tục dùng luồng xác thực hiện có. API phát email nghiệp vụ chỉ dành cho caller service/admin; không cho trình duyệt tự gửi email hay tự gán danh tính service.

| Luồng | REST endpoint |
|---|---|
| OTP | `POST /api/v1/notifications/email/otp` |
| Xác nhận đổi/reset mật khẩu | `POST /api/v1/notifications/password-changes` |
| Duyệt/từ chối Seller | `POST /api/v1/notifications/seller-decisions` |
| Ban/unban | `POST /api/v1/notifications/account-status-changes` |

OTP purpose lần lượt là `registration`, `password_reset`, `seller_registration`. Caller truyền request ID ổn định cho cùng yêu cầu retry, user ID và issued/expiry; mã mới có request ID mới. Nghiệp vụ nguồn commit trước khi yêu cầu email xác nhận. Các caller/FE thuộc repo khác phải được triển khai hợp đồng tương ứng; pull riêng Notification không tự thêm caller còn thiếu.

`NOTIFICATION_DELIVERY_WORKER_ENABLED` điều khiển worker email/Push nghiệp vụ; OTP có worker riêng. Chỉ bật worker nghiệp vụ sau khi rà hàng đợi và SMTP. Cấu hình Firebase là phần riêng nếu test Push.

## Kiểm chứng email thật

Đăng ký trên FE -> nhận OTP -> gửi lại -> mã cũ bị từ chối -> nhập mã mới -> tài khoản được xác minh. Sau đó test OTP quên mật khẩu và từng email nghiệp vụ. Ghi request/event/delivery ID và xác nhận hộp thư; không ghi OTP, JWT, mật khẩu hoặc bundle khóa.

HTTP200/Queued chỉ là tiếp nhận. `ProviderAccepted`/`Sent` vẫn cần đối chiếu hộp thư thật và Spam. SMTP hết quota/sai thông tin hoặc bản worker cũ còn chạy phải được ghi là BLOCKED/FAIL.

## Database và kiểm thử

DB server dùng kết nối riêng được cung cấp; startup migration mặc định **tắt**. Người phụ trách kiểm tra schema và backup trước khi bật migration; thành viên không reset/seed DB chung.

```powershell
dotnet build Notification.sln -c Release
dotnet test Notification.sln -c Release
```

Các test PostgreSQL yêu cầu `NOTIFICATION_TEST_DB` trỏ tới PostgreSQL kiểm thử tách biệt. Không dùng kết nối DB chung cho test tạo/xóa DB. Các file `.env*` riêng, appsettings thật, `.local-backups`, key rings và credentials không được commit; chỉ file `*.example*` chứa placeholder được chia sẻ.

## Dừng và khởi động lại

```powershell
docker compose stop notification-service
docker compose up -d notification-service
```

Giữ nguyên DB và volume. Khi đổi ba file cấu hình, chạy `docker compose up -d --build notification-service` để container nhận cấu hình mới.
