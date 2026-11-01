# Notification Service — Integration Guide

> **Base URL:** `http://<host>:8007`  
> **Version:** `v1`  
> **Protocol:** REST / JSON over HTTP  
> **Phiên bản tài liệu:** 2026-11-01

---

## Mục Lục

1. [Tổng quan](#1-tổng-quan)
2. [Authentication & Headers](#2-authentication--headers)
3. [Cấu trúc Response chuẩn](#3-cấu-trúc-response-chuẩn)
4. [API Public — Người dùng cuối](#4-api-public--người-dùng-cuối)
5. [API Internal — Service-to-Service Email](#5-api-internal--service-to-service-email)
6. [API Admin — Quản trị](#6-api-admin--quản-trị)
7. [Enum Reference](#7-enum-reference)
8. [Error Codes Reference](#8-error-codes-reference)
9. [Luồng tích hợp mẫu](#9-luồng-tích-hợp-mẫu)

---

## 1. Tổng Quan

Notification Service chịu trách nhiệm **duy nhất** cho:

| Chức năng | Mô tả |
|-----------|-------|
| **In-App Notification** | Tạo, lấy, đánh dấu đã đọc thông báo trong ứng dụng cho từng user |
| **Email (OTP)** | Gửi email xác minh OTP theo purpose (đăng ký, reset mật khẩu, seller) |
| **Email (General)** | Gửi email HTML tùy ý từ các service backend |
| **Email (Seller rejection)** | Gửi email thông báo từ chối đơn đăng ký seller |
| **Device Token** | Đăng ký / thu hồi token thiết bị phục vụ Push Notification |
| **Notification Preferences** | Cho phép user tắt/bật từng loại thông báo theo từng kênh |

> **Quan trọng:** Notification Service **không** phát sinh sự kiện ra ngoài. Nó nhận yêu cầu qua REST và thực thi đồng bộ. Không dùng RabbitMQ, MassTransit, hay gRPC.

---

## 2. Authentication & Headers

### 2.1 Public API (user-facing)

Tất cả endpoint public yêu cầu API Gateway forward **header định danh user** vào request:

| Header | Kiểu | Bắt buộc | Mô tả |
|--------|------|----------|-------|
| `x-user-id` | `string (UUID)` | ✅ | ID của user hiện tại (Guid) |
| `x-user-roles` | `string` | Tuỳ | Danh sách role, cách nhau bằng dấu phẩy. VD: `buyer,seller` |
| `x-user-email` | `string` | Tuỳ | Email user (nếu cần) |

```http
GET /api/v1/notifications/me HTTP/1.1
Host: notification-service:8007
x-user-id: 3fa85f64-5717-4562-b3fc-2c963f66afa6
x-user-roles: buyer
```

> ⚠️ Nếu thiếu `x-user-id`, API trả về `401 Unauthorized`.

### 2.2 Internal API (service-to-service)

Tất cả endpoint tại `/api/v1/internal/*` yêu cầu header secret key:

| Header | Kiểu | Bắt buộc | Mô tả |
|--------|------|----------|-------|
| `X-Internal-Service-Key` | `string` | ✅ | Shared secret key cấu hình qua biến môi trường `INTERNAL_SERVICE_SECRET` |

```http
POST /api/v1/internal/notifications/email/otp HTTP/1.1
Host: notification-service:8007
X-Internal-Service-Key: <secret>
Content-Type: application/json
```

> 🔴 Sai / thiếu key → `403 Forbidden`. Không được gọi endpoint này từ frontend.

### 2.3 Admin API

Endpoint admin yêu cầu header `x-user-roles` chứa `admin` hoặc `staff`:

```http
GET /api/v1/admin/notifications HTTP/1.1
x-user-id: <admin-uuid>
x-user-roles: admin
```

---

## 3. Cấu Trúc Response Chuẩn

### 3.1 Response thành công

Không có wrapper envelope. API trả thẳng object / array / paged data:

```json
// Object đơn
{
  "id": "3fa85f64-...",
  "title": "...",
  "isRead": false
}
```

```json
// Danh sách phân trang (PagedData<T>)
{
  "items": [ ... ],
  "pagination": {
    "pageIndex": 1,
    "pageSize": 20,
    "totalCount": 150,
    "totalPages": 8,
    "hasPreviousPage": false,
    "hasNextPage": true
  }
}
```

### 3.2 Response lỗi (RFC 9110 Problem Details)

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Not found",
  "status": 404,
  "errorCode": "NOTIFICATION_NOT_FOUND",
  "detail": "Notification not found or does not belong to the user.",
  "traceId": "00-abc123-def456-00"
}
```

### 3.3 Response lỗi Validation (400)

```json
{
  "errorCode": "VAL_INVALID_EMAIL",
  "message": "Recipient email is required and must be a valid email address."
}
```

> **Quy tắc enum trong JSON:** Tất cả enum trả về dưới dạng **PascalCase string** — `"OrderPaid"`, `"Completed"`, `"InApp"`, v.v. Không dùng số nguyên.

---

## 4. API Public — Người Dùng Cuối

Base path: `/api/v1/notifications`

---

### 4.1 Lấy danh sách thông báo in-app

```
GET /api/v1/notifications/me
GET /api/v1/notifications
```

**Query Parameters:**

| Tham số | Kiểu | Mặc định | Mô tả |
|---------|------|----------|-------|
| `isRead` | `boolean?` | null | Lọc theo trạng thái đã đọc (`true`/`false`). Bỏ trống = lấy tất cả |
| `notificationType` | `string?` | null | Lọc theo loại. Xem [§7.1](#71-notificationtype) |
| `page` | `integer` | 1 | Số trang (≥ 1) |
| `pageSize` | `integer` | 20 | Số item mỗi trang (1–100) |

**Response `200 OK`:**

```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "userId": "a1b2c3d4-0000-0000-0000-000000000001",
      "title": "Đơn hàng #1234 đã được thanh toán",
      "body": "Đơn hàng của bạn đang được chuẩn bị.",
      "payload": "{\"orderId\": \"...\"}",
      "isRead": false,
      "readAt": null,
      "notificationType": "OrderPaid",
      "status": "Completed",
      "createdAt": "2026-11-01T10:30:00+00:00"
    }
  ],
  "pagination": {
    "pageIndex": 1,
    "pageSize": 20,
    "totalCount": 5,
    "totalPages": 1,
    "hasPreviousPage": false,
    "hasNextPage": false
  }
}
```

---

### 4.2 Đánh dấu 1 thông báo đã đọc

```
PATCH /api/v1/notifications/{id}/read
PUT   /api/v1/notifications/{id}/read
```

**Path Parameter:** `id` — UUID của thông báo.

**Response `200 OK`:**

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "Đơn hàng #1234 đã được thanh toán",
  "body": "Đơn hàng của bạn đang được chuẩn bị.",
  "isRead": true,
  "createdAt": "2026-11-01T10:30:00+00:00",
  "readAt": "2026-11-01T10:45:00+00:00"
}
```

**Response `404 Not Found`:**

```json
{
  "errorCode": "NOTIFICATION_NOT_FOUND",
  "message": "Notification not found or does not belong to the user."
}
```

---

### 4.3 Đánh dấu hàng loạt thông báo đã đọc

```
PATCH /api/v1/notifications/me/read
PUT   /api/v1/notifications/me/read
```

**Request Body:**

```json
// Đánh dấu các thông báo CỤ THỂ
{
  "notificationIds": [
    "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "7cb96a75-0000-0000-0000-000000000002"
  ]
}
```

```json
// Đánh dấu TẤT CẢ thông báo chưa đọc
{
  "notificationIds": null
}
```

**Response `200 OK`:**

```json
{
  "markedCount": 3
}
```

---

### 4.4 Lấy cấu hình thông báo của tôi

```
GET /api/v1/notifications/preferences
```

**Response `200 OK`:**

```json
[
  {
    "id": "...",
    "notificationType": "OrderPaid",
    "channelCode": "InApp",
    "isDisabled": false
  },
  {
    "id": "...",
    "notificationType": "Promotion",
    "channelCode": "Email",
    "isDisabled": true
  }
]
```

---

### 4.5 Cập nhật cấu hình thông báo

```
PUT /api/v1/notifications/preferences
```

**Request Body:**

```json
{
  "preferences": [
    {
      "notificationType": "OrderPaid",
      "channelCode": "InApp",
      "isDisabled": false
    },
    {
      "notificationType": "Promotion",
      "channelCode": "Email",
      "isDisabled": true
    }
  ]
}
```

> `notificationType` và `channelCode` là string PascalCase. Xem [§7](#7-enum-reference).

**Response `200 OK`:**

```json
{
  "updatedCount": 2
}
```

---

### 4.6 Đăng ký device token (Push Notification)

```
POST /api/v1/notifications/devices
```

**Request Body:**

```json
{
  "token": "fcm-token-string-here",
  "platform": "Web"
}
```

| Field | Kiểu | Bắt buộc | Giá trị hợp lệ |
|-------|------|----------|---------------|
| `token` | `string` | ✅ | FCM / APNs token |
| `platform` | `string` | ❌ | `"Web"` (mặc định), `"Ios"`, `"Android"` |

**Response `200 OK`:**

```json
{
  "platform": "Web",
  "token": "fcm-token-string-here",
  "registeredAt": "2026-11-01T10:00:00+00:00"
}
```

---

### 4.7 Thu hồi device token (khi logout)

```
DELETE /api/v1/notifications/devices/{token}
```

**Path Parameter:** `token` — chuỗi FCM / APNs token.

**Response `204 No Content`** — kể cả token không tồn tại.

---

## 5. API Internal — Service-to-Service Email

Base path: `/api/v1/internal/notifications/email`

> 🔴 **Tất cả endpoint này phải gọi từ backend service nội bộ** qua header `X-Internal-Service-Key`.  
> Không được expose ra ngoài API Gateway. Không được gọi từ frontend.

---

### 5.1 Gửi email OTP

```
POST /api/v1/internal/notifications/email/otp
```

**Request Body:**

```json
{
  "email": "user@example.com",
  "fullName": "Nguyen Van A",
  "otpCode": "123456",
  "expiresInMinutes": 5,
  "purpose": "registration"
}
```

| Field | Kiểu | Bắt buộc | Mặc định | Mô tả |
|-------|------|----------|----------|-------|
| `email` | `string` | ✅ | — | Email người nhận (hợp lệ) |
| `fullName` | `string?` | ❌ | `"Customer"` | Tên hiển thị trong email |
| `otpCode` | `string` | ✅ | — | Mã OTP |
| `expiresInMinutes` | `integer` | ❌ | `5` | Thời hạn hết hạn (1–60 phút) |
| `purpose` | `string?` | ❌ | `"registration"` | Loại OTP — xem bảng dưới |

**Giá trị `purpose` hợp lệ:**

| Giá trị | Subject email được gửi |
|---------|----------------------|
| `"registration"` | `Nexus - Your Verification Code (OTP)` |
| `"password_reset"` | `Nexus - Password Reset Verification Code` |
| `"seller_registration"` | `Nexus - Seller Account Verification Code` |

**Response `200 OK`:**

```json
{
  "success": true,
  "message": "OTP verification email dispatched successfully.",
  "data": {
    "recipient": "user@example.com",
    "fullName": "Nguyen Van A",
    "expiresInMinutes": 5,
    "purpose": "registration",
    "dispatchedAt": "2026-11-01T10:00:00+00:00"
  }
}
```

**Response `400 Bad Request`:**

```json
{
  "errorCode": "VAL_INVALID_EMAIL",
  "message": "Recipient email is required and must be a valid email address."
}
```

```json
{
  "errorCode": "VAL_INVALID_OTP",
  "message": "OTP code cannot be empty."
}
```

**Response `500 Internal Server Error`:**

```json
{
  "errorCode": "EMAIL_SEND_FAILED",
  "message": "Failed to deliver OTP email via notification provider."
}
```

---

### 5.2 Gửi email tổng quát (HTML tùy ý)

```
POST /api/v1/internal/notifications/email/send
```

**Request Body:**

```json
{
  "toEmail": "user@example.com",
  "subject": "Đơn hàng của bạn đã được xác nhận",
  "htmlBody": "<h1>Xin chào!</h1><p>Đơn hàng #1234 đã được xác nhận.</p>"
}
```

| Field | Kiểu | Bắt buộc | Mô tả |
|-------|------|----------|-------|
| `toEmail` | `string` | ✅ | Email người nhận |
| `subject` | `string` | ✅ | Tiêu đề email |
| `htmlBody` | `string` | ✅ | Nội dung HTML đầy đủ |

**Response `200 OK`:**

```json
{
  "success": true,
  "message": "Email dispatched successfully.",
  "data": {
    "recipient": "user@example.com",
    "subject": "Đơn hàng của bạn đã được xác nhận",
    "dispatchedAt": "2026-11-01T10:00:00+00:00"
  }
}
```

---

### 5.3 Gửi email từ chối seller registration

```
POST /api/v1/internal/notifications/email/seller-rejection
```

**Request Body:**

```json
{
  "email": "seller@example.com",
  "fullName": "Nguyen Van B",
  "shopName": "Shop ABC",
  "rejectReason": "Giấy phép kinh doanh không hợp lệ."
}
```

| Field | Kiểu | Bắt buộc | Mô tả |
|-------|------|----------|-------|
| `email` | `string` | ✅ | Email seller |
| `fullName` | `string?` | ❌ | Tên seller (`"Customer"` nếu không có) |
| `shopName` | `string?` | ❌ | Tên shop (`"Your Shop"` nếu không có) |
| `rejectReason` | `string` | ✅ | Lý do từ chối (hiển thị trực tiếp trong email) |

**Response `200 OK`:**

```json
{
  "success": true,
  "message": "Seller rejection email dispatched successfully.",
  "data": {
    "recipient": "seller@example.com",
    "shopName": "Shop ABC",
    "rejectReason": "Giấy phép kinh doanh không hợp lệ.",
    "dispatchedAt": "2026-11-01T10:00:00+00:00"
  }
}
```

---

## 6. API Admin — Quản Trị

Base path: `/api/v1/admin/notifications`  
Yêu cầu: `x-user-roles: admin` hoặc `x-user-roles: staff`

---

### 6.1 Tìm kiếm lịch sử thông báo

```
GET /api/v1/admin/notifications
```

**Query Parameters:**

| Tham số | Kiểu | Mô tả |
|---------|------|-------|
| `userId` | `UUID?` | Lọc theo user |
| `notificationType` | `string?` | Lọc theo loại. Xem [§7.1](#71-notificationtype) |
| `status` | `string?` | Lọc theo trạng thái. Xem [§7.2](#72-notificationstatus) |
| `page` | `integer` | Số trang (mặc định: 1) |
| `pageSize` | `integer` | Kích thước trang (mặc định: 20, tối đa: 100) |

**Response `200 OK`:** `PagedData<InAppNotificationItemResponse>` — cấu trúc giống [§4.1](#41-lấy-danh-sách-thông-báo-in-app).

---

## 7. Enum Reference

> Tất cả enum truyền vào và nhận về đều là **string PascalCase**. Không dùng số nguyên.

### 7.1 NotificationType

| Giá trị | Mô tả |
|---------|-------|
| `OrderPaid` | Đơn hàng đã thanh toán |
| `OrderShipped` | Đơn hàng đã giao |
| `AuctionOutbid` | Bị outbid trong phiên đấu giá |
| `AuctionWon` | Thắng phiên đấu giá |
| `ReputationPenalty` | Phạt điểm uy tín |
| `SecurityAlert` | Cảnh báo bảo mật |
| `Otp` | Thông báo OTP |
| `Promotion` | Khuyến mãi |

### 7.2 NotificationStatus

| Giá trị | Mô tả |
|---------|-------|
| `Pending` | Đang chờ xử lý |
| `Completed` | Đã gửi thành công |
| `Partial` | Gửi một phần kênh thành công |
| `Failed` | Gửi thất bại |

### 7.3 ChannelType (dùng trong Preferences)

| Giá trị | Mô tả |
|---------|-------|
| `Email` | Kênh email |
| `Push` | Push notification |
| `InApp` | Thông báo trong ứng dụng |
| `Sms` | SMS |

### 7.4 DevicePlatform (dùng khi đăng ký device)

| Giá trị | Mô tả |
|---------|-------|
| `Web` | Trình duyệt web (mặc định) |
| `Ios` | iOS |
| `Android` | Android |

### 7.5 DeliveryStatus

| Giá trị | Mô tả |
|---------|-------|
| `Pending` | Chờ gửi |
| `Sending` | Đang gửi |
| `Sent` | Đã gửi |
| `Failed` | Gửi thất bại |
| `Skipped` | Bỏ qua (user đã tắt kênh) |

---

## 8. Error Codes Reference

| HTTP Status | Error Code | Ý nghĩa |
|-------------|------------|---------|
| `400` | `VAL_INVALID_EMAIL` | Email không hợp lệ hoặc thiếu |
| `400` | `VAL_INVALID_OTP` | OTP code rỗng |
| `400` | `VAL_INVALID_INPUT` | Dữ liệu đầu vào thiếu hoặc sai format |
| `400` | `VAL_INVALID_USER_ID` | `x-user-id` header thiếu hoặc không phải UUID |
| `400` | `VAL_INVALID_ARGUMENT` | Tham số không hợp lệ (chung) |
| `401` | `UNAUTHORIZED` | Thiếu `x-user-id` header |
| `403` | `FORBIDDEN` | Không có quyền truy cập (sai role hoặc sai `X-Internal-Service-Key`) |
| `404` | `NOTIFICATION_NOT_FOUND` | Thông báo không tồn tại hoặc không thuộc user này |
| `500` | `EMAIL_SEND_FAILED` | SMTP gửi thất bại |
| `500` | `INTERNAL_SERVER_ERROR` | Lỗi server không xác định |
| `500` | `INTERNAL_CONFIGURATION_ERROR` | Chưa cấu hình `INTERNAL_SERVICE_SECRET` |

---

## 9. Luồng Tích Hợp Mẫu

### Luồng 1 — Frontend: Hiển thị notification bell

```
1. User mở notification bell icon
   → FE gọi GET /api/v1/notifications/me?isRead=false&page=1&pageSize=10
   → Render danh sách thông báo chưa đọc + badge count

2. User click vào 1 thông báo
   → FE gọi PATCH /api/v1/notifications/{id}/read
   → Cập nhật UI (badge count - 1)

3. User nhấn "Đọc tất cả"
   → FE gọi PATCH /api/v1/notifications/me/read với body { "notificationIds": null }
   → Badge count = 0
```

### Luồng 2 — UserService: Gửi OTP đăng ký tài khoản

```
1. User đăng ký tài khoản → UserService tạo OTP code
2. UserService gọi:
   POST /api/v1/internal/notifications/email/otp
   Header: X-Internal-Service-Key: <secret>
   Body:   { "email": "...", "fullName": "...", "otpCode": "123456", "purpose": "registration" }
3. Notification Service gửi email qua SMTP
4. User nhận email OTP
```

### Luồng 3 — UserService / AuthService: Reset mật khẩu

```
1. User yêu cầu reset mật khẩu → AuthService tạo OTP
2. AuthService gọi:
   POST /api/v1/internal/notifications/email/otp
   Body: { "email": "...", "otpCode": "...", "purpose": "password_reset" }
```

### Luồng 4 — SellerService: Từ chối seller registration

```
1. Admin từ chối đơn đăng ký seller
2. SellerService gọi:
   POST /api/v1/internal/notifications/email/seller-rejection
   Header: X-Internal-Service-Key: <secret>
   Body:   { "email": "...", "fullName": "...", "shopName": "...", "rejectReason": "..." }
3. Notification Service gửi email thông báo từ chối
```

### Luồng 5 — OrderService: Thông báo in-app sau thanh toán

```
1. Payment thành công → OrderService cần thông báo cho user
2. OrderService gọi endpoint tạo in-app notification (internal):
   Liên hệ team Notification để lấy endpoint nội bộ tương ứng.
```

### Luồng 6 — Frontend: Cài đặt thông báo

```
1. User vào trang cài đặt
   → FE gọi GET /api/v1/notifications/preferences
   → Render toggle cho từng NotificationType × ChannelType

2. User tắt email Promotion
   → FE gọi PUT /api/v1/notifications/preferences
   Body: {
     "preferences": [
       { "notificationType": "Promotion", "channelCode": "Email", "isDisabled": true }
     ]
   }
```

---

## Phụ Lục — Cấu Hình Môi Trường

| Biến môi trường | Mô tả |
|-----------------|-------|
| `INTERNAL_SERVICE_SECRET` | Shared secret key cho Internal API |
| `SMTP__Host` | SMTP server host |
| `SMTP__Port` | SMTP port (thường `587`) |
| `SMTP__Username` | SMTP username |
| `SMTP__Password` | SMTP password |
| `SMTP__UseSsl` | `true` / `false` |
| `SMTP__SenderName` | Tên hiển thị người gửi |
| `SMTP__SenderEmail` | Địa chỉ email gửi |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string (port `5439`, db `notification_db`) |

> **Simulation mode:** Nếu `SMTP__Username` hoặc `SMTP__Password` trống, service sẽ **giả lập** gửi email (log warning) thay vì báo lỗi — tiện cho dev/test local không có SMTP thật.

---

*Tài liệu sinh từ source code thực tế — commit `7b93f33` — 2026-11-01.*
