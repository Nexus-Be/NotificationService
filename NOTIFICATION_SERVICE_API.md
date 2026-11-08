> Current email contract (2026-11-07): email routes are public only: POST /api/v1/notifications/email/otp, /send, /seller-rejection. Legacy /api/v1/internal/notifications/email/* routes are removed. Responses are pure Application DTOs; OTP reports Queued when enqueued and preserves purpose through the worker. Seller approval has no endpoint yet. See docs/api/API_EMAIL_OWNERSHIP_20261107.md for the verified change; historical examples below may differ. Internal in-app is outside this fix.

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
| **Email (Seller approval)** | Gửi email chúc mừng thông báo phê duyệt đơn đăng ký seller |
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

### 5.4 Gửi email phê duyệt seller registration (Approval)

```
POST /api/v1/internal/notifications/email/seller-approval
```

**Request Body:**

```json
{
  "email": "seller@example.com",
  "fullName": "Nguyen Van B",
  "shopName": "Shop ABC",
}
```

| Field | Kiểu | Bắt buộc | Mô tả |
|-------|------|----------|-------|
| `email` | `string` | ✅ | Email seller |
| `fullName` | `string?` | ❌ | Tên seller (`"Customer"` nếu không có) |
| `shopName` | `string?` | ❌ | Tên shop (`"Your Shop"` nếu không có) |

**Response `200 OK`:**

```json
{
  "success": true,
  "message": "Seller approval email dispatched successfully.",
  "data": {
    "recipient": "seller@example.com",
    "shopName": "Shop ABC",
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

### Luồng 4 — SellerService: Phê duyệt hoặc từ chối seller registration

```
1. Admin phê duyệt đơn đăng ký seller:
   SellerService gọi:
   POST /api/v1/internal/notifications/email/seller-approval
   Header: X-Internal-Service-Key: <secret>
   Body:   { "email": "...", "fullName": "...", "shopName": "..." }
   → Notification Service gửi email chúc mừng thành công.

2. Hoặc Admin từ chối đơn đăng ký seller:
   SellerService gọi:
   POST /api/v1/internal/notifications/email/seller-rejection
   Header: X-Internal-Service-Key: <secret>
   Body:   { "email": "...", "fullName": "...", "shopName": "...", "rejectReason": "..." }
   → Notification Service gửi email thông báo từ chối.
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

## Template-based notification dispatch (2026-11-07)
POST /api/v1/notifications/dispatches accepts SourceEventId, UserId, TemplateCode, Locale, Data and optional Email Destination. See docs/api/Dispatches/POST_DISPATCHES.md for request JSON, mandatory rules, idempotency, errors, verification and migration preflight. Email deliveries are stored Pending and processed by DatabaseEmailWorker; InApp is delivered immediately. Existing raw in-app requests do not automatically use this template flow.


## Durable database Email delivery (2026-11-07)
Template-backed Pending Email deliveries are now processed by DatabaseEmailWorker. Default limit is 3 total attempts with exponential backoff, RowVersion ownership fencing and expired-lease recovery. Existing direct email/OTP endpoints are unchanged. Missing SMTP credentials fail on this worker instead of simulation success. See docs/architecture/DATABASE_EMAIL_WORKER.md for settings, status semantics, operational limits and verification. No new migration or endpoint was added in this stage.

## FCM Push delivery (2026-11-07)
POST /api/v1/notifications/dispatches now supports ORDER_PAID_PUSH and AUCTION_OUTBID_PUSH. Omit Destination; active owned FCM devices are snapshotted into independent deliveries. Response adds deliveries while retaining original fields. Durable worker handles Email and Push with per-device retries and guarded UNREGISTERED cleanup. Set FCM_PROJECT_ID and provide server ADC before real sends. See docs/api/Dispatches/POST_DISPATCHES_PUSH.md and docs/architecture/FCM_PUSH.md. Latest verified suite: 131 passed, none failed/skipped; no real FCM send or project database migration performed.

## Notification management APIs (2026-11-07)
Ten operations under /api/v1/admin/notifications cover template list/detail/create/update, channel list/update, delivery list/detail/logs and durable manual retries. Admin/Staff read; Admin only mutates. Updates require expectedUpdatedAt; retry requires requestId, expectedRowVersion and reason, returns 202 acceptance, and persists idempotency with audit/log/state atomically. Details and JSON examples: [management contract](docs/api/Management/NOTIFICATION_MANAGEMENT.md). Migration 20261107163605_AddDeliveryRetryRequests is additive and has not been applied to the project DB. Latest Release build: 0 warnings/errors; 143 tests passed, none failed/skipped. No live provider sends or deployment.

## Event Logs REST (2026-11-08)

Added POST /api/v1/notifications/event-logs (Service/Admin,201 new or200 replay), GET /api/v1/admin/notifications/event-logs and GET /{id} (Admin/Staff). Contract and examples: docs/api/EventLogs/POST_EVENT_LOGS.md. EventLogsController/AdminEventLogsController use the Application EventLogs feature, existing Gateway identity policy and no internal key/broker.

Stores structured payload, source trace and whitelisted correlation headers. SQL list filters/pagination exclude payload/headers; restricted detail returns JSON. Source/event unique key and canonical SHA256 envelope fingerprint handle replay/conflicts and simultaneous requests. Legacy hashless rows are readable but cannot be overwritten; soft-deleted keys remain reserved.

Migration 20261107175502_AddEventLogIngestion adds3 nullable columns and6 indexes with designer/snapshot. Review duplicate existing keys and historical source casing before applying; no auto-cleanup or project DB migration. Release build0 warnings/errors;157 tests pass,0 fail/skip, including14 new isolated Kestrel/PostgreSQL tests. Corrected body-limit413 mapping and scoped old management Swagger test to its controller tag. Aggregate GitNexus CRITICAL includes previous uncommitted stages; new files reviewed manually.

No real providers, credentials edits, commit/deploy. Producers still need REST integration; SourceService is producer-reported, Gateway must protect service identities. Full payload is operator-restricted and not automatically redacted. Retention90days/partition cleanup and health remain pending.

## Event Logs90-day retention —2026-11-08

Implemented EventLogRetentionService and EventLogRetentionWorker with opt-in Enabled=false default. Retains90 UTC days from server CreatedAt (strict <cutoff); includes soft-hidden/legacy rows, never AuditLogs or notification/delivery records. Default batch500,max20/interval3600seconds,run timeout120seconds. Atomic parameterized SKIP LOCKED deletes, fresh scoped runs, cancellation/error recovery and aggregate-only logs. Previously committed batches survive later errors; locked rows deferred.

Additive migration20261107181007_AddEventLogRetentionIndex (+designer/snapshot) adds(CreatedAt,Id) only. No project DB migration/delete. New settings are in example JSON/.env/Compose; actual runtime configuration unchanged. Direct .NET uses EventLogRetention__Enabled; Compose maps NOTIFICATION_EVENT_LOG_RETENTION_ENABLED. Review existing data/policy/backups and apply migration before enabling.

Release build0 errors/warnings;180 tests passed,0failed/skipped, including23 new retention cases (10 PostgreSQL,13 host/settings). Verified strict microsecond boundary, UTC/producer-time distinction, preservation of unrelated tables, bounded batching, competing workers/locks, cancel/failure partial progress, atomic failed statement, replay after purge, DI/disabled/timeouts/shutdown/scopes. Owned temporary PostgreSQL cleaned after tests. No live provider, credentials edits, commit or deployment. GitNexus aggregate CRITICAL includes prior stages; new files reviewed manually.

Contract: docs/architecture/EVENT_LOG_RETENTION.md. Physical purge releases source/event dedup key, so idempotency lasts while row is retained. Cleanup asynchronous with possible backlog; DELETE disk reclamation depends on normal PostgreSQL maintenance. Existing table remains non-partitioned; monthly partition-drop portion of SRS pending separate reviewed migration design. Next stage: readiness/health for database and workers.

## Readiness and worker health —2026-11-08

Preserved GET /health liveness200; added GET /health/ready anonymous sanitized200 Ready/503 NotReady and GET /api/v1/admin/notifications/health-checks Admin/Staff detail200/503. Contract: docs/api/Health/GET_HEALTH_CHECKS.md. All health responses no-store. Public probe omits worker names/timestamps/errors; detail exposes stable codes only, no raw exception/connection/payload/config.

Scoped database probe with linked timeout checks connection, pending migrations and critical EventLogs/delivery SQL access. Thread-safe singleton registry tracks Delivery/OTP/Retention lifecycle and staleness, disabled workers excluded. Worker iteration failure differs from ordinary provider delivery failure. OTP queue waiting legitimate indefinitely; processing stale threshold configured. Existing workers report finally Stopped, with optional reporter constructor compatibility. OTP unexpected errors now back off1second to prevent a tight failure loop.

No entity/schema/migration or health DB writes. Existing /api/v1/admin/health/broker remains legacy history read, not runtime readiness; no broker checker introduced. Example Health settings3/30/120 and Compose/env mappings added; actual settings/secrets unchanged. Configuration and operation limits documented, including enabled retention failure blocking readiness until next successful run.

Release build0 errors/warnings;208 tests passed,0failed/skipped, including28 new cases (18 state/host/settings,10 HTTP/PostgreSQL). All prior180 preserved. Tests cover roles/status/privacy/no-store/Swagger, migration/schema/timeout/recovery/caller cancel/read-only, concurrent state updates, all three actual worker lifecycles and delivery loop failure. Initial connection-failure test raced equal1second deadlines; driver1second now precedes probe3seconds to test unavailable classification deterministically.

No project DB migration/cleanup, real provider calls, credentials edits, commit or deploy. Owned test PostgreSQL cleaned. Aggregate GitNexus CRITICAL includes prior stages; untracked files reviewed manually. Queue metrics/history/dashboard, full schema/write/provider validation and cluster aggregation remain pending. Next business work: preferences/mandatory and unread behavior from SRS.

## Preferences/mandatory/read states —2026-11-08

GET/PUT /api/v1/notifications/preferences completed: effective default matrix without writes, mandatory/default/channel-disabled flags, nullable ID for virtual rows, strict whole-batch validation, per-user transaction advisory serialization, active uniqueness, atomic audit and no-op replay. Changed updatedCount semantics count actual changed rows. Supported Email/Push/InApp only. Critical types and active mandatory template pairs apply consistently to preference APIs, dispatch and legacy in-app; globally disabled channels still prevent sends.

New PUT /api/v1/notifications/{id}/read-states accepts required isRead, owned visible rows only; idempotent IsRead/ReadAt mutation, keeps status/deliveries/attempts/RowVersion unchanged. Existing read aliases/internal in-app route preserved as legacy, no new internal endpoint. Contract: docs/api/Preferences/PREFERENCES_READ_STATES.md.

Migration20261107184347_AddActivePreferenceUniqueness (+designer/snapshot) only adds active composite unique index, with soft-delete filter in model. Review active duplicates before migration, do not auto-delete history. No project database migration. Release0 errors/warnings,223 tests pass,0fail/skip;15 new Kestrel/PostgreSQL tests plus208 previous tests preserved. Optional fixture changed from critical OrderPaid to OrderShipped; default-list fixture seeds channels and expects virtual matrix. Input attributes corrected to record constructor parameters for MVC.

No real providers/sends, project DB edits, credentials changes, commit/deploy. Owned temporary PostgreSQL cleaned after verification. Aggregate GitNexus CRITICAL includes all earlier changes; UNKNOWN/interface edges source-reviewed and covered by HTTP/EF tests. Client must consume nullable default IDs/flags; defaults virtual rather than registration-time physical provisioning, concurrent different writes last writer wins. Full business template mapping and legacy/audit device/OTP work remain backlog.

## Device registration/refresh/rotation —2026-11-08

POST /api/v1/notifications/devices keeps response platform/token/registeredAt and adds optional previousToken. Strict platform names/default Web, required bounded opaque token, MVC+Swagger validation,16KiB/no-store. Non-positional request record retains compatible constructors. New partial UserNotificationService.Devices serializes token operations with sorted SHA256 advisory keys and row locks.

Active token cannot be taken from another owner; explicit revoke releases inactive token for account transfer. Deleted registration not revived, invalid previous token404. Rotation old revoke/new update/audit atomic; existing unique index catches outside writers with409 and full rollback. Revoke checks fresh owner state and keeps no-op204 for foreign/unknown/inactive. Refresh retains device ID and advances timestamps >=1microsecond, protecting against stale FCM invalidation. Audit DeviceRegister/DeviceRevoke stores fingerprints and state changes, never raw token. Client still must report token refresh/logout.

Release0 errors/warnings;237 tests pass,0fail/skip.14 new Kestrel/PostgreSQL tests cover validation/auth/defaults/ownership/rotation,8 concurrent refreshes, opposite rotations, revoke/account-transfer race, audit privacy/rollback, outside unique conflict, stale FCM callback and old pending delivery skip. All223 previous tests retained; account-switch fixture now revokes first. Existing schema/enum/unique index unchanged, no migration/model drift.

Contract: docs/api/Devices/POST_DEVICES.md. No project DB/real provider/credentials change/commit/deploy; owned test PostgreSQL cleaned. Aggregate GitNexus CRITICAL includes prior uncommitted stages; new file manually reviewed. Actual device/client integration remains pending. User reverted broad all-checklist request; this session deliberately completes only the device step, next work is OTP audit and legacy APIs.


## Protected durable OTP — 2026-11-08

Completed Notification-side registration/password-reset/resend delivery as the first core business step. POST /api/v1/notifications/email/otp now requires trusted Gateway service/admin identity,16KiB/no-store; optional requestId/userId/issuedAt/expiresAt/traceId preserve older payload fields. New GET /api/v1/notifications/otp-dispatches/{id} returns masked metadata. Stable ID+canonical body replay; changed fields409. Relative expiry replay retains original acceptance; producers should supply stable IDs and explicit challenge times. New different-ID challenge must be strictly newer within recipient+purpose, else409 OTP_REQUEST_OBSOLETE.

OtpEmailDispatches stores Data Protection encrypted payload and separately protected fingerprint, integer states, expiry/attempt/lease/version metadata. Pending/sending older challenge superseded atomically; terminal payload erased. Audit OtpAccepted8/OtpAttempt9/OtpResult10 (existing0..7 unchanged) is committed with state; no OTP/body/ciphertext/fingerprint in audit/EventLogs/inbox/status. Group advisory locks and version fences protect replay/resend/concurrent claim/late callback. Existing DB schema history remains intact; migration20261108095031_AddProtectedOtpDispatches only creates the table and3 indexes, with designer/snapshot. Not applied to project DB.

Runtime DI replaces RAM OTP queue/worker with DurableOtpWorker; standalone old constructors/classes remain compatibility only. Strict SMTP sender never simulates success. Purpose-specific encoded content and absolute UTC expiry; timeout bounded by remaining TTL, max3 total attempts/backoff5/10seconds within TTL, lease120seconds. Host stop/crash leaves recoverable Sending; stale callback cannot overwrite supersession/reclaim. SMTP crash-after-acceptance can duplicate: stable MessageId is not exactly-once. ProviderAccepted is not verification, reading, or final inbox receipt.

Data Protection application name Nexus.Notification.Otp, separate purpose strings; optional OtpProtection:KeyRingPath. Compose persistent notification-otp-keys volume and example OtpWorker settings added; persist/share/protect/backup keys across restarts/replicas before rollout. Missing/wrong key fails closed. OTP terminal metadata/fingerprint retention is separate future work; EventLog90day cleanup does not purge these rows. Database readiness now checks OTP schema; health wait budget tracks polling and active budget accounts for send timeout. Provider delivery failures do not falsely signal an unhealthy worker loop.

Release build0 errors/warnings;257 tests pass,0failed/skipped, preserving237 earlier cases plus20 new OTP/host cases. Real isolated PostgreSQL/Kestrel, fake strict sender, actual persistent test key ring across provider instances; covers auth/validation/Swagger/privacy,8-way replay and competing workers, cross-group key conflict, resend/purpose isolation, expiry/retry/timeout/permanent error, shutdown/lease, stale-result fencing, atomic audit rollback, wrong key and host failure/recovery/stop. GitNexus aggregate CRITICAL reflects all earlier uncommitted stages; ambiguous DI/interface/framework edges and new files manually reviewed. No project DB/provider/credentials changes, commit or deployment.

Contract/handoff: docs/api/Otp/OTP_DELIVERY.md. User Service still owns generation/verification/hash, cooldown/rate-limit, old-code invalidation, password commit/session revocation and anti-enumeration responses. User/Gateway integration and real SMTP receipt remain unverified. Next core step is password-change/reset-success confirmation after User commit, followed by Seller decisions/ban-unban, OrderPaid Buyer/Seller+bell grouping, then auction Push. Those workflows are not completed by this OTP step.


## Password-change/reset-success confirmations — 2026-11-08

Completed Notification-side POST /api/v1/notifications/password-changes, service/admin through trusted Gateway,16KiB/no-store. Source sends stable sourceEventId,target userId,email,name,kind Changed/Reset,changedAt after account update commits; unknown JSON members rejected, no password/hash/OTP accepted. Source account ownership/authentication, commit/session revocation and source retry remain caller responsibilities. Notification never verifies or changes passwords.

Two vi-VN mandatory Email/SecurityAlert templates PASSWORD_CHANGED_EMAIL and PASSWORD_RESET_SUCCEEDED_EMAIL (deterministic IDs600...006/007), encoded FullName and absolute UTC change time, contact-support instruction if unauthorized. New PasswordChangeDispatch receipt reserves source event ID across recipient/kind with canonical nonsecret body SHA256; no raw payload. Event advisory transaction lock serializes acceptance and replay/conflict; shared template row lock keeps channel/type/mandatory validation stable while reusing DispatchService. Generic dispatch with the same key but no matching business receipt is rejected rather than adopting unverified content.

Atomic dispatch+delivery/log+receipt+PasswordChangeAccepted11 audit (existing0..10 unchanged). Same normalized body replays current persisted delivery state even if template later inactive/receipt soft-hidden; changed fields409 PASSWORD_CHANGE_CONFLICT. New business templates must remain active Email/SecurityAlert/mandatory. Critical preference cannot suppress; global channel disable produces persisted Skipped and no automatic requeue after re-enable. Existing DatabaseEmailWorker handles strict SMTP/retry/lease/fencing, and operator delivery/log/retry APIs are reused. New acceptance reports Pending, worker Sent only means SMTP accepted, not recipient read. SMTP duplication after crash still possible. Existing Email NotificationRecord can appear in current inbox projection; no second InApp/Push channel/grouping created in this step.

Additive migration20261108100603_AddPasswordChangeConfirmations: receipt table with restrictive notification FK, unique notification index,2 new templates and designer/snapshot; no update/delete of existing data. Review code/locale and deterministic-ID collisions before rollout; project DB not migrated. Receipt retention is separate future policy, not covered by EventLog cleanup.

Verification: Release build0 warnings/errors;274 tests pass,0failed/skipped, preserving257 baseline plus17 new Kestrel/PostgreSQL cases. Tests cover changed/reset content, auth/strict unknown-secret/size/validation/Swagger/no-store, current-status replay and trace/normalization,8-way duplicate and conflicting-target races, audit failure rolling back previously saved dispatch, mandatory/global disable, unsafe/inactive template/missing channel/placeholder errors, transient/permanent provider results, generic-dispatch collision and template-edit lock. Actual DeliveryService used with fake sender, no real provider call. The lock assertion accounts for Npgsql wrapping transient55P03 inside InvalidOperationException. GitNexus aggregate CRITICAL includes all prior uncommitted work; DI/framework edges UNKNOWN manually followed and new files reviewed.

Handoff: docs/api/PasswordChanges/POST_PASSWORD_CHANGES.md. Controller PasswordChangesController; Application IPasswordChangeService/PasswordChangeService and strict CreatePasswordChangeRequest/PasswordChangeResponse; Domain PasswordChangeDispatch; persistence/config/seeds/migration above; RBAC service/admin; errors and enum mappings in contract. No credentials edits, live sends, project DB migration, commit or deployment. Owned disposable PostgreSQL stopped after tests. Next checklist step: Seller approval/rejection, then ban/unban, followed by OrderPaid Buyer/Seller+bell and auction Push. Source integration and real SMTP receipt remain pending for both OTP and password confirmation.


## Seller application decision emails — 2026-11-08

Completed Notification-side POST /api/v1/notifications/seller-decisions, Service/Admin trusted Gateway,16KiB/no-store. Source Staff reviews through account service, calls only after APPROVED+Seller role or REJECTED+reason transaction commits. Notification neither grants role nor creates accounts/shops or approves products. Recipient/email/application/outcome remain trusted source responsibility. REST only.

Strict request sourceEventId/applicationId/userId,email,shopName,decision Approved/Rejected,decidedAt; optional fullName/trace/rejectionReason. Rejected requires user-safe bounded reason, Approved omits it; unknown JSON members rejected including bank/document/CCCD/password/OTP data. Source must remove sensitive documents/bank data from reason text; HTML encoding is not PII redaction. Two vi-VN mandatory SecurityAlert5/Email0 templates SELLER_APPROVED_EMAIL/SELLER_REJECTED_EMAIL IDs600...008/009, encoded name/shop/time/reason and no invented links/deadlines. Existing notification enum/matrix unchanged.

SellerDecisionDispatch event PK,unique ApplicationId,unique NotificationId restrictive FK,canonical nonsecret SHA256 RequestHash,base/softdelete. Event then application advisory locks seed8011, shared template row lock and atomic dispatch/delivery/log/receipt/SellerDecisionAccepted12 audit; previous0..11 unchanged. Same stable event+canonical payload replays current state; changed fields409 SELLER_DECISION_CONFLICT. Different event ID on same application409 SELLER_APPLICATION_ALREADY_NOTIFIED, including aliases or opposing outcomes and soft-hidden receipts; source maps legacy event names to one committed stable ID. Resubmission requires new application identity. Generic dispatch without a matching business receipt is not adopted.

Reuses existing durable Email worker/provider retry/lease/fencing, operator delivery/log/manual retry. Pending is DB acceptance, Sent means SMTP accepted not recipient read or role verification. Critical preferences cannot suppress; global disabled Email yields persisted Skipped, replay does not requeue automatically after re-enable. Legacy direct seller-email paths remain compatibility only; producer must call the business endpoint exclusively to avoid dual sends. One Email record may appear in current owner-scoped inbox projection; no additional InApp/Push or grouping promised. SMTP duplicates still possible across crash-after-provider-acceptance; dedicated receipt retention remains future policy.

Migration20261108101858_AddSellerDecisionNotifications only creates receipt table/FK/2 unique indexes and inserts2 templates, with designer/snapshot. No existing rows updated/deleted. Check target seed ID/code+locale collisions and apply migration through reviewed rollout; project DB not migrated. Release build0 warnings/errors;295 tests passed,0failed/skipped, preserving274 baseline plus21 new actual Kestrel/PostgreSQL tests. Covers approval/rejection/encoded reason/privacy, strict fields/roles/size/Swagger/no-store, canonical replay/inactive template/hidden receipt,8-way replay,event/application races,alias reservation/new application,atomic audit rollback,mandatory/global disable,template lock/metadata/channel/placeholder errors and real DeliveryService fake sender retry/permanent outcomes.

GitNexus aggregate CRITICAL reflects all previous uncommitted stages; new files/framework/DI edges manually inspected and covered by tests (pre-edit DbContext MEDIUM,contract/seed LOW,DI/enum UNKNOWN). No credentials edits,live provider/source integration,project DB migration,commit or deployment. Owned disposable PostgreSQL stopped after verification. Contract/handoff docs/api/SellerDecisions/POST_SELLER_DECISIONS.md; next step account ban/unban, followed by OrderPaid Buyer/Seller+bell and auction Push.


## Account ban/unban emails — 2026-11-08

Completed Notification-side POST /api/v1/notifications/account-status-changes,Service/Admin trusted Gateway,16KiB/no-store. Source Staff owns actual violation ban/unban,account status commit and ban session revocation. Notification only sends after source commit,never changes status/roles/sessions or performs temporary login lock,deletion,automatic unban. Email primary and does not require banned recipient login.

Strict request sourceEventId,userId,positive int64 statusVersion,email,change Banned/Unbanned,required user-safe reason for both,changedAt;optional name/trace. Unknown JSON blocks password/OTP/bank/documents/arbitrary data fields. Source must exclude sensitive PII from reason text; HTML encoding is not redaction. Two vi-VN mandatory SecurityAlert5/Email0 templates ACCOUNT_BANNED_EMAIL/ACCOUNT_UNBANNED_EMAIL,IDs600...00a/00b,encoded name/reason/effective UTC time,distinct suspension/restoration wording,no invented deadline/link. Existing enum/default-preference matrix unchanged.

AccountStatusChangeDispatch event PK,unique(UserId,StatusVersion),unique notification restrictive FK,canonical hash64/base/softdelete. Event then user-version locks seed8012; shared template lock validates active mandatory Email/SecurityAlert across dispatch. Atomic existing dispatch/delivery/log+receipt+AccountStatusChangeAccepted13 audit (old0..12 unchanged),audit excludes full reason/raw payload. Same event/body replay persisted status; changed409 ACCOUNT_STATUS_CONFLICT; different event same account-revision409 ACCOUNT_STATUS_ALREADY_NOTIFIED including aliases/soft-hidden receipts. New source revision supports ban→unban→reban; historical lower revisions retained without inferring current status/sequence. Source revision is committed account-status revision,not timestamp,attempt number or newly generated retry value. Rejected unmatched generic dispatch cannot be adopted as known business content.

Existing strict durable Email worker/retry/lease/fencing and operator history/log/manual retry reused with no shared behavior changed. Pending means committed queue; Sent means SMTP accepted,not read or proof of source state. Critical preference cannot suppress; global disabled Email remains persisted Skipped/replay does not requeue. Existing Email NotificationRecord may appear in owner inbox projection;no separate InApp/Push. SMTP crash duplicates and receipt retention remain limitations.

Migration20261108104312_AddAccountStatusNotifications only adds receipt table/FK/2 unique indexes and2 templates,designer/snapshot synchronized;no existing rows updated/deleted. Check target seed ID/code+locale collisions before reviewed rollout; project DB not migrated. Release build0 warnings/errors;317 tests passed,0failed/skipped,preserving295 prior and22 new actual Kestrel/PostgreSQL cases: both content/reasons,auth/validation/size/unknown/privacy/Swagger/no-store,canonical replay,inactive template/hidden receipt,8-way replay,event/revision races,alias reservation,cycles/delayed history/account-scoped versions,atomic rollback,mandatory/global disable,template lock/metadata/channel/render errors,real DeliveryService fake provider transient/permanent outcomes.

GitNexus aggregate CRITICAL includes all prior uncommitted work; new files/DI/framework edges manually inspected (pre-edit DbContext MEDIUM,contract/seed LOW,DI/enum UNKNOWN). No actual source/provider integration,project DB/credentials edits,live sends,commit/deploy. Owned disposable PostgreSQL stopped after tests. Handoff docs/api/AccountStatusChanges/POST_ACCOUNT_STATUS_CHANGES.md; next OrderPaid Buyer/Seller+bell logical-message grouping,then auction Push. Those are not implemented by account-status notifications.


## OrderPaid Buyer/Seller logical inbox — 2026-11-08

Completed Notification-side POST /api/v1/notifications/order-payments Service/Admin,16KiB/no-store,after Commerce commits one regular single-seller suborder PAID. Strict source event/order/shop/buyer/seller IDs,orderNo,VND amount18,2,paidAt and authoritative Seller deadline. Buyer/Seller distinct; source owns payment/recipient/shop/amount/deadline correctness and reliable REST retry. No source order/wallet mutation,broker or gRPC. Multi-shop checkout invokes separately for each seller suborder.

One transaction creates two logical NotificationRecords (Buyer/Seller),one InApp delivery each and Push deliveries per active owner device. New mandatory OrderPaid/Push templates ORDER_PAID_BUYER_PUSH/ORDER_PAID_SELLER_PUSH IDs600...00c/00d supply same plain-text content for each branch's InApp/Push. Buyer confirms payment/amount;Seller receives paid order/UTC accept deadline without Buyer total or wallet-credit claim. JSON payload audience/order/shop/time/currency,Buyer-only totalAmount,no arbitrary URL. Deadline never restarted,old events/deadlines accepted with original effective time. Operator template data for Seller deliberately excludes Buyer TotalAmount.

OrderPaidDispatch event PK,unique OrderId,unique Buyer/Seller notification restrictive FKs,canonical hash64/base/softdelete. Event then order locks seed8013,shared template locks,atomic receipt/notifications/deliveries/logs/OrderPaymentAccepted14 audit (old0..13 unchanged). Same event+canonical body replays same IDs/current states (amount normalized ignoring decimal scale,UTC microseconds,trace excluded);changed409 ORDER_PAYMENT_CONFLICT;different event same order409 ORDER_ALREADY_NOTIFIED. Generic dispatch key collision rolls back whole new pair rather than adopting unknown content. Replay does not fanout newly registered devices.

NotificationRecord.IsInboxVisible true in CLR/database with ValueGeneratedNever preserves old rows/inserts while explicit false persists. Personal list/unread/single/batch/read-states honor it;operator history/worker do not. InApp enabled yields one unread bell item per branch even without Push token,with many devices or failed Push. Global InApp disabled leaves active Push-only record outside bell;global Push disabled/missing tokens/over100 devices generate Skipped marker,not acceptance failure. Over100 skips only that branch Push with PUSH_DEVICE_LIMIT;other branch and inbox survive. Both channels skipped hide record. Worker unchanged,existing per-device ownership/retry/lease/fencing/UNREGISTERED semantics retained;InApp Sent prevents failed Push erasing inbox. Critical user preferences cannot suppress;global channel disable applies at acceptance/worker as existing policy.

New owner GET /api/v1/notifications/order-payments/{notificationId} returns structured detail for notificationId already carried by FCM data;anonymous401,foreign/deleted/non-order item404,owner Push-only allowed,GET never marks read. Frontend must render text safely,resolve payload audience/order/shop to its route,and recheck Commerce permissions. Backend badge/read/unread ownership tested;no UI/client or real FCM receipt claimed.

Migration20261108111246_AddOrderPaymentNotifications adds flag(default true),receipt table/2 restrictive FKs/3 unique indexes,2 template inserts,and sets new flag true on existing seeded notification. Up reviewed: no existing column/table drop or data deletion;EF scaffolding warning reviewed,generated Down removes only this migration's additions and was not executed. Snapshot/designer match model. Check target seed ID/code+locale collisions before reviewed rollout;project DB not migrated.

Release build0 warnings/errors;342 tests pass,0failed/skipped,preserving317 prior plus25 new Kestrel/PostgreSQL cases. Covers two owners/one item,many/no/reassigned/over-limit devices,channel combinations/mandatory,Push isolation/retry/no resend,privacy/owner detail/read-unread/badge,strict HTTP/Swagger/no-store,replay/decimal normalization/hidden receipt/inactive template,8-way replay/event-order races,rollback/bad Seller template/generic collision,shared template lock,past deadline,legacy visibility/model. New tests use real DeliveryService and fake Push;no provider/client/source integration. Health fault-injection test now stops actual OTP reporter before manually setting registry states,eliminating live-worker overwrite race without production worker changes. Initial deadline assertion adjusted to documented PostgreSQL microsecond normalization.

GitNexus pre-edit NotificationRecord CRITICAL,DbContext MEDIUM,seed/contract LOW,DI/interface edges UNKNOWN manually followed;aggregate CRITICAL includes prior uncommitted stages. New files/shared read paths manually reviewed and covered by full regression. No project DB/credentials edits,live sends,commit/deploy. Owned disposable PostgreSQL stopped after tests. Contract/handoff docs/api/OrderPayments/ORDER_PAYMENTS.md. Commerce source/retry,target rollout,client bell/FCM/deep links and real device receipt,receipt retention remain pending. Next backend core step auction Push;full UI/end-to-end checkbox remains open.


## Auction BidOutbid logical notification — 2026-11-08

Completed first auction backend substep only: POST /api/v1/notifications/auction-outbids Service/Admin,16KiB/no-store,after a valid higher bid commits. Target previous highest bidder;strict different previous/new bidder/Seller and bid IDs,positive increasing VND prices,source effective time/end snapshot. Notification never places bids,determines winners,charges/penalizes or decides current auction state. Source owns actual valid bid/recipient/anti-sniping/end-time/REST retry. Winner/cancel/timeout remain pending.

One logical NotificationRecord with allowed InApp+per-device Push deliveries,using optional vi-VN AUCTION_BID_OUTBID_PUSH template600...00e (AuctionOutbid2/Push1),legacy AUCTION_OUTBID_PUSH unchanged. Per-channel user opt-out honored unless its active mandatory pair overrides;Push mandatory never forces InApp,global channel disable always wins. Reuses IsInboxVisible grouping/owner inbox/read states and existing strict worker/FCM/ownership/retry/lease/fencing. No token/failed Push preserves allowed InApp;over100 devices skips only Push. All channels suppressed hides record;replay does not change suppression/fanout when preferences/devices change.

AuctionOutbidDispatch event PK,unique TriggerBidId/PreviousBidId/NotificationId,auction index,restrictive notification FK,canonical proof64/base/softdelete. Event→trigger→previous locks seed8014,template FOR SHARE,atomic notification/delivery/log/receipt/AuctionOutbidAccepted15 audit (old0..14 unchanged). Same normalized body/event replay current state;changed409 AUCTION_OUTBID_CONFLICT;new event reusing either bid409 AUCTION_BID_ALREADY_NOTIFIED,including hidden receipts. Fresh bid pairs may notify same user again. Unmatched generic dispatch collision rejected/rolled back.

Owner GET /api/v1/notifications/auction-outbids/{notificationId} returns structured payload to resolve FCM notificationId/inbox action,foreign/new bidder/Seller/deleted/non-outbid404,anonymous401. Push-only detail allowed,GET not mark read. Payload auction/bid IDs,price/effective time/end,no opponent/Seller identity/raw token. Historical notice uses price recorded at OccurredAt,not a claim of live price/still-open auction;source snapshot remains immutable on retry. Client must recheck Auction current state/access and render text safely. Live price/countdown/bid-list streaming is separate from this notification.

Additive migration20261108113033_AddAuctionOutbidNotifications creates receipt table/FK/4 indexes (3 unique)+1 optional template;no existing data changed,designer/snapshot synchronized. Target seed-ID/code+locale collision checks before reviewed rollout;project DB not migrated. Release build0 warnings/errors;370 tests pass,0failed/skipped,342 baseline retained plus28 new Kestrel/PostgreSQL cases. Target/privacy/multi-device grouping,optional/mandatory/global policies independently per channel,owner detail/read/badge,no/overflow/reassigned devices,provider failure/retry,replay/normalization/hidden/inactive,8-way concurrency,bid aliases/both bid keys/repeat valid pairs,rollback/template lock/config/render/generic collision,late history/Swagger/model covered. Real DeliveryService with fake FCM,not actual device/source integration.

GitNexus pre-edit DbContext MEDIUM,contract/seed LOW,DI/enum UNKNOWN manually followed;aggregate CRITICAL includes prior uncommitted work,new files reviewed. No shared worker/OrderPaid behavior change,project DB/credentials edits,live provider,commit/deploy. Owned disposable PG stopped after tests. Contract/handoff docs/api/AuctionOutbids/AUCTION_OUTBIDS.md. Real Auction call/retry,client/FCM receipt,latency measurements and dedicated receipt retention remain pending;configurable polling is not a realtime SLA,late Push may arrive after end. Next winner/AuctionSettled payment instructions email and grouped notifications,then cancellation/timeout.


## AuctionSettled/Winner payment instructions — 2026-11-08

Completed Notification-side POST /api/v1/notifications/auction-winners Service/Admin,16KiB/no-store,after final Auction winning result commits. Strict auction/result/winning-bid/Winner/Seller/contact/price/source-time/deadline snapshot;Winner differs from Seller. Optional OrderId because AuctionSettled can precede Commerce order creation. No source result/order/payment/wallet mutation or claim of paid;winning bid price not necessarily final Commerce total. Source owns result/recipient/contact and reliable immutable REST retry. Winner only;Seller result,cancel/timeout remain pending.

One AuctionWon logical item with InApp,required Email and per-device optional Push. New vi-VN AUCTION_WINNER_EMAIL mandatory Email(ID600...00f),AUCTION_WINNER_PUSH optional Push(ID600...010). HTML email encoded name/title/source UTC deadline,plain Push/InApp;instructions open related auction/order in Nexus,check final amount/current eligible order before payment. No arbitrary link/QR/bank fields or invented order creation. Past deadlines retain source time and conditional payment wording. Payload auction/result/bid/order optional/price/time,no Seller identity or Winner email. Owner GET /api/v1/notifications/auction-winners/{notificationId} resolves existing FCM notificationId,foreign/deleted/non-winner404,anonymous401,Email/Push-only allowed,GET no read mutation. Client must recheck Auction/Commerce state/permissions and render plain text.

Added nullable NotificationDelivery.SubjectOverride500/BodyOverride4000 snapshot fields;shared Email worker uses overrides or parent fallback,Push unchanged. Winner Email has immutable HTML override,sibling channels share plain parent;retry/manual retry preserves snapshot even after template edit. No arbitrary override API or raw content exposure added. Existing deliveries null preserve parent behavior. Mandatory AuctionWon/Email adds one effective mandatory preference pair (10/24 vs9/24),known defaults fixture adjusted;Push/InApp optional unless their own mandatory pair,global disable always wins. Missing/over100 devices suppress only Push,not Email/InApp. One bell item,mixed provider failures preserve successful channels and InApp;all channels suppressed hidden.

AuctionWinnerDispatch event PK,unique auction/result/winning-bid/notification,restrictive FK,hash64/base/softdelete. Event→auction→result→bid locks seed8015,shared template locks,atomic group/receipt/log/AuctionWinnerAccepted16 audit(old0..15 stable). Same canonical snapshot replay current states;changed409 AUCTION_WINNER_CONFLICT,new event reusing result identity409 AUCTION_RESULT_ALREADY_NOTIFIED across hidden receipts. Fresh auction/result/bid can notify same Winner again. Generic Push dispatch collision rolled back,never adopt unknown content;replay does not enrich null order or reset deadlines/devices.

Migration20261108114401_AddAuctionWinnerNotifications adds receipt table/FK/4 unique indexes,2 nullable delivery columns,2 templates and sets only new override fields null on2 seeded deliveries. Up reviewed additive,no old data/column/table deletion;generated Down not executed. Snapshot/designer match model,seed-ID/code+locale target collision review required before rollout. Project DB not migrated. Release0 errors/warnings;398 tests pass,0failed/skipped,370 retained plus28 new real PG/Kestrel cases. Covers recipient/privacy/grouping,HTML vsplain encoding,absent order/past deadline,mandatory/optional/global policies,owner/read-badge/devices/overflow,provider isolation/retry/manual retry with frozen snapshots,legacy null fallback,validation/Swagger,replay/aliases/concurrent recipients/results,rollback/template lock/config/render/generic collision/model. Test-only assertions decode encoded Vietnamese and advance fake clock past monotonic manual-retry timestamp.

GitNexus pre-edit NotificationDelivery CRITICAL,DbContext MEDIUM,seed/contract LOW,DI/worker/framework edges UNKNOWN manually followed;aggregate CRITICAL includes prior uncommitted stages. Shared fallback reviewed and full regression retained;no Push protocol/worker claim behavior changed. No actual source/SMTP/FCM/client integration,project DB/credentials edits,live sends,commit/deploy. Owned test PG stopped after verification. Handoff docs/api/AuctionWinners/AUCTION_WINNERS.md. Target rollout/current-state integration/real receipt/latency and receipt retention remain pending;provider crash duplication possible. Next cancellation/timeout;Seller result notification still separately pending.
