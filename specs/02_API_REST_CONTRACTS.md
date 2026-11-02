# 02 — HỢP ĐỒNG GIAO TIẾP RESTFUL (NOTIFICATION SERVICE REST CONTRACTS)

> **Trạng thái triển khai (2026-11-02):** ✅ ĐÃ HOÀN TẤT & HOẠT ĐỘNG THẬT — Đầy đủ **11 endpoint RESTful công khai** qua 4 controllers: `NotificationsController`, `EmailController`, `AdminNotificationsController`, `AdminHealthController`. Bãi bỏ và loại bỏ hoàn toàn gRPC, RabbitMQ MassTransit và các route internal theo Quy chuẩn 12.
> **Nguồn sự thật:** code (`Notification.Api/Controllers/Public/`).
> **Cập nhật lần cuối:** 2026-11-02.

Tài liệu này đặc tả toàn bộ hợp đồng giao tiếp đồng bộ **RESTful API (JSON over HTTP)** thuộc phạm vi của **Notification Service**. Toàn bộ giao tiếp sử dụng phương thức HTTP chuẩn RFC 9110, nhận và trả JSON chuẩn `camelCase`, phản hồi bọc thống nhất trong `ApiResponse<T>` tại biên Gateway hoặc Pure DTO tại service.

---

## 1. DANH SÁCH ENDPOINTS RESTFUL ĐANG HOẠT ĐỘNG (11 ENDPOINTS)

### 1.1. Quản lý Thiết Bị, Thông Báo In-App & Cài Đặt (`NotificationsController`)

| # | HTTP Method | Endpoint Route | Mô Tả Nghiệp Vụ | DTO Request / Response | Quyền / Header | Trạng Thái |
|:--|:---|:---|:---|:---|:---|:---:|
| 1 | `POST` | `/api/v1/notifications/devices` | Đăng ký hoặc làm mới FCM/APNs device token | `RegisterDeviceRequest` / `RegisterDeviceResponse` | `x-user-id` | ✅ Hoạt động thật |
| 2 | `DELETE` | `/api/v1/notifications/devices/{token}` | Hủy đăng ký device token khi logout | N/A / `204 NoContent` | `x-user-id` | ✅ Hoạt động thật |
| 3 | `GET` | `/api/v1/notifications/preferences` | Lấy tùy chọn kênh nhận thông báo cá nhân | N/A / `UserPreferenceResponse` | `x-user-id` | ✅ Hoạt động thật |
| 4 | `PUT` | `/api/v1/notifications/preferences` | Cập nhật tùy chọn nhận thông báo cá nhân | `UpdatePreferencesRequest` / `UserPreferenceResponse` | `x-user-id` | ✅ Hoạt động thật |
| 5 | `GET` | `/api/v1/notifications/me` | Lấy danh sách lịch sử thông báo in-app (phân trang) | Query params / `PagedData<NotificationDto>` | `x-user-id` | ✅ Hoạt động thật |
| 6 | `PATCH` | `/api/v1/notifications/me/read` | Đánh dấu đọc hàng loạt thông báo | `BatchMarkReadRequest` / `BatchMarkReadResponse` | `x-user-id` | ✅ Hoạt động thật |

### 1.2. Dịch Vụ Gửi Email Trực Tiếp & OTP (`EmailController`)

| # | HTTP Method | Endpoint Route | Mô Tả Nghiệp Vụ | DTO Request / Response | Header Yêu Cầu | Trạng Thái |
|:--|:---|:---|:---|:---|:---:|:---:|
| 7 | `POST` | `/api/v1/notifications/email/otp` | Gửi email chứa mã OTP (4 trường chuẩn: email, fullName, otpCode, expiresInMinutes) | `SendOtpEmailApiRequest` / JSON object | N/A | ✅ Hoạt động thật |
| 8 | `POST` | `/api/v1/notifications/email/send` | Gửi email thông báo đơn lẻ theo định dạng HTML | `SendGeneralEmailApiRequest` / JSON object | N/A | ✅ Hoạt động thật |
| 9 | `POST` | `/api/v1/notifications/email/seller-rejection` | Gửi email từ chối duyệt hồ sơ người bán kèm lý do | `SendSellerRejectionEmailApiRequest` / JSON object | N/A | ✅ Hoạt động thật |

### 1.3. Quản Trị Hệ Thống & Kiểm Tra Sức Khỏe Hàng Đợi (`AdminNotificationsController`, `AdminHealthController`)

| # | HTTP Method | Endpoint Route | Mô Tả Nghiệp Vụ | DTO Request / Response | Quyền / Header | Trạng Thái |
|:--|:---|:---|:---|:---|:---|:---:|
| 10 | `GET` | `/api/v1/admin/notifications` | Quản trị viên tra cứu lịch sử thông báo toàn hệ thống | Query params / `PagedData<InAppNotificationItemResponse>` | `admin`, `staff` | ✅ Hoạt động thật |
| 11 | `GET` | `/api/v1/admin/health/broker` | Kiểm tra trạng thái và độ trễ của message broker / queue | N/A / `IReadOnlyList<BrokerHealthCheckResponse>` | `admin`, `staff` | ✅ Hoạt động thật |

### 1.4. Dịch Vụ Gửi Email Nội Bộ Cho Microservices (`InternalEmailController`)

| # | HTTP Method | Endpoint Route | Mô Tả Nghiệp Vụ | DTO Request / Response | Header Yêu Cầu | Trạng Thái |
|:--|:---|:---|:---|:---|:---:|:---:|
| 12 | `POST` | `/api/v1/internal/notifications/email/otp` | Gửi email OTP nội bộ có hỗ trợ trường purpose (`registration`, `password_reset`, ...) | `InternalSendOtpEmailApiRequest` / JSON object | N/A | ✅ Hoạt động thật |
| 13 | `POST` | `/api/v1/internal/notifications/email/send` | Gửi email HTML chung qua route nội bộ | `InternalSendGeneralEmailApiRequest` / JSON object | N/A | ✅ Hoạt động thật |
| 14 | `POST` | `/api/v1/internal/notifications/email/seller-rejection` | Gửi email từ chối duyệt người bán qua route nội bộ | `InternalSendSellerRejectionEmailApiRequest` / JSON object | N/A | ✅ Hoạt động thật |

---

## 2. QUY CHUẨN XỬ LÝ LỖI & MÃ LỖI

Toàn bộ phản hồi lỗi tuân thủ nghiêm ngặt **RFC 9110** và quy chuẩn hệ thống:
1. **100% tiếng Anh**, câu thông báo viết chuẩn **Sentence case**.
2. **Mã lỗi SCREAMING_SNAKE_CASE**:
   - `VAL_INVALID_EMAIL`: Email không hợp lệ hoặc thiếu.
   - `VAL_INVALID_OTP`: Mã OTP rỗng hoặc thiếu.
   - `VAL_INVALID_INPUT`: Dữ liệu đầu vào không hợp lệ hoặc thiếu trường bắt buộc.
   - `UNAUTHORIZED`: Thiếu hoặc token / thông tin người dùng không hợp lệ (`USER_ID_REQUIRED`).
   - `FORBIDDEN`: Không có quyền truy cập hoặc thao tác trên tài nguyên.
   - `NOTIFICATION_NOT_FOUND`: Không tìm thấy bản ghi thông báo hoặc tùy chọn người dùng.
   - `EMAIL_SEND_FAILED`: Gửi email SMTP thất bại từ phía notification provider.
   - `INTERNAL_SERVER_ERROR`: Lỗi hệ thống nội bộ.

---

## 3. GIAO DIỆN KIỂM THỬ SWAGGER UI

Swagger UI được tích hợp sẵn phục vụ việc kiểm thử độc lập cho Notification Service:
- **Địa chỉ truy cập**: `http://localhost:8007/swagger` (hoặc mở trực tiếp `http://localhost:8007/`, hệ thống sẽ tự động chuyển hướng đến `/swagger`).
- **OpenAPI JSON Spec**: `http://localhost:8007/swagger/v1/swagger.json`.
- **Nút Authorize**: Hỗ trợ nhập JWT Bearer token và header `x-user-id` trực quan.
- **Cấu hình bật/tắt**: Kiểm soát qua `EnableSwagger` trong `appsettings.json` (mặc định bật trong Development).

---

## 4. LỊCH SỬ BÃI BỎ GRPC, MESSAGE BROKER & TẠM KHÓA INTERNAL API

> **Thông báo bãi bỏ:** 
> - Hợp đồng gRPC (`NotificationApi` trong `shop.proto`) đã bị **loại bỏ hoàn toàn**. Service không còn lắng nghe trên gRPC port và không phụ thuộc vào `Grpc.AspNetCore`.
> - Cơ chế RabbitMQ / MassTransit và Consumer (`SendOtpEmailConsumer`) đã được **bãi bỏ hoàn toàn**. Toàn bộ giao tiếp liên service sử dụng 100% RESTful HTTP clients đồng bộ.
> - Tuân thủ **Quy chuẩn 12 (Tạm khóa Internal API)**: Toàn bộ endpoint được phục vụ qua route công khai `/api/v1/notifications/...`, không dùng controller nội bộ `/api/v1/internal`.
