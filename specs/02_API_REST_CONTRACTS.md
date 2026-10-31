# 02 — HỢP ĐỒNG GIAO TIẾP RESTFUL (NOTIFICATION SERVICE REST CONTRACTS)

> **Trạng thái triển khai (2026-10-27):** ✅ ĐÃ HOÀN TẤT & HOẠT ĐỘNG THẬT — Đầy đủ **9 endpoint RESTful** qua 2 controllers: `NotificationsController` và `EmailController`. Bãi bỏ và loại bỏ hoàn toàn gRPC (`NotificationGrpcService` và `shop.proto` đã bị xóa bỏ).
> **Nguồn sự thật:** code (`Notification.Api/Controllers/NotificationsController.cs`, `Notification.Api/Controllers/EmailController.cs`, `Notification.Api/Program.cs`).
> **Cập nhật lần cuối:** 2026-10-27.

Tài liệu này đặc tả toàn bộ hợp đồng giao tiếp đồng bộ **RESTful API (JSON over HTTP)** thuộc phạm vi của **Notification Service**. Toàn bộ giao tiếp sử dụng phương thức HTTP chuẩn RFC 9110, nhận và trả JSON chuẩn `camelCase`, phản hồi bọc thống nhất trong `ApiResponse<T>`.

---

## 1. DANH SÁCH ENDPOINTS RESTFUL ĐANG HOẠT ĐỘNG (9 ENDPOINTS)

### 1.1. Quản lý Thiết Bị, Thông Báo In-App & Cài Đặt (`NotificationsController`)

| # | HTTP Method | Endpoint Route | Mô Tả Nghiệp Vụ | DTO Request / Response | Quyền / Header | Trạng Thái |
|:--|:---|:---|:---|:---|:---|:---:|
| 1 | `POST` | `/api/v1/notifications/devices` | Đăng ký thiết bị nhận push | `RegisterDeviceRequest` | `x-user-id` | ✅ Hoạt động thật |
| 2 | `DELETE` | `/api/v1/notifications/devices/{token}` | Hủy đăng ký thiết bị push | Token trên route | `x-user-id` | ✅ Hoạt động thật |
| 3 | `GET` | `/api/v1/notifications/preferences` | Lấy tùy chọn nhận thông báo | `UserPreferenceResponse` | `x-user-id` | ✅ Hoạt động thật |
| 4 | `PUT` | `/api/v1/notifications/preferences` | Cập nhật tùy chọn thông báo | `UpdatePreferencesRequest` | `x-user-id` | ✅ Hoạt động thật |
| 5 | `GET` | `/api/v1/notifications/me` | Lấy danh sách thông báo cá nhân | `ListUserNotificationsRequest` | `x-user-id` | ✅ Hoạt động thật |
| 6 | `PUT` | `/api/v1/notifications/me/read` | Đánh dấu thông báo đã đọc | `MarkReadRequest` | `x-user-id` | ✅ Hoạt động thật |
| 7 | `GET` | `/api/v1/notifications/admin/monitoring` | Dashboard giám sát kênh thông báo | N/A | Admin | ✅ Hoạt động thật |

### 1.2. Dịch Vụ Gửi Email Trực Tiếp & OTP (`EmailController`)

| # | HTTP Method | Endpoint Route | Mô Tả Nghiệp Vụ | DTO Request / Response | Header Yêu Cầu | Trạng Thái |
|:--|:---|:---|:---|:---|:---:|:---:|
| 8 | `POST` | `/api/v1/notifications/email/otp` | Gửi email chứa mã OTP (đăng ký/đăng nhập/quên mật khẩu) | `SendOtpEmailRequest` | N/A | ✅ Hoạt động thật |
| 9 | `POST` | `/api/v1/notifications/email/send` | Gửi email đơn hoặc hàng loạt theo mẫu | `SendEmailRequest` | N/A | ✅ Hoạt động thật |

---

## 2. QUY CHUẨN XỬ LÝ LỖI & MÃ LỖI

Toàn bộ phản hồi lỗi tuân thủ nghiêm ngặt **RFC 9110** và quy chuẩn hệ thống:
1. **100% tiếng Anh**, câu thông báo viết chuẩn **Sentence case**.
2. **Mã lỗi SCREAMING_SNAKE_CASE**:
   - `USER_ID_REQUIRED`: Thiếu header hoặc param `x-user-id`.
   - `INVALID_EMAIL`: Email không hợp lệ.
   - `OTP_CODE_REQUIRED`: Mã OTP rỗng hoặc thiếu.
   - `SEND_EMAIL_FAILED`: Gửi email SMTP thất bại.
   - `NOT_FOUND`: Không tìm thấy bản ghi thông báo hoặc tùy chọn người dùng.

---

## 3. GIAO DIỆN KIỂM THỬ SWAGGER UI

Swagger UI được tích hợp sẵn phục vụ việc kiểm thử độc lập cho Notification Service:
- **Địa chỉ truy cập**: `http://localhost:8007/swagger` (hoặc mở trực tiếp `http://localhost:8007/`, hệ thống sẽ tự động chuyển hướng đến `/swagger`).
- **OpenAPI JSON Spec**: `http://localhost:8007/swagger/v1/swagger.json`.
- **Nút Authorize**: Hỗ trợ nhập JWT Bearer token và header `x-user-id` trực quan.
- **Cấu hình bật/tắt**: Kiểm soát qua `EnableSwagger` trong `appsettings.json` (mặc định bật trong Development).

---

## 4. LỊCH SỬ BÃI BỎ GRPC (GRPC DEPRECATION NOTICE)

> **Thông báo bãi bỏ:** Hợp đồng gRPC (`NotificationApi` trong `shop.proto`) đã bị **loại bỏ hoàn toàn**. Service không còn lắng nghe trên gRPC port và không phụ thuộc vào `Grpc.AspNetCore`. Mọi giao tiếp đồng bộ đều qua HTTP RESTful port `8007`. Giao tiếp sự kiện qua RabbitMQ MassTransit tiếp tục duy trì (`SendOtpEmailConsumer`).
