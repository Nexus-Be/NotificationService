# NOTIFICATION SERVICE - POST /api/v1/notifications/devices

> **Feature / Use Case:** Notifications / Register Device
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-02
> **Tác giả:** AI Coding Agent

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/notifications/devices`
- **Quyền hạn (RBAC Policy):** Mọi người dùng đã xác thực (`User` / `Buyer` / `Seller` / `Admin`)
- **Quy tắc Sở hữu (Resource Ownership):** Gắn token thiết bị với `userId` được trích xuất từ header `x-user-id` (hoặc token claims).
- **Controller:** `Notification.Api/Controllers/Public/NotificationsController.cs` -> `RegisterDevice()`
- **Application Contract:** `Notification.Application/Services/Notifications/IUserNotificationService.cs` -> `RegisterDeviceAsync()`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
*Không có tham số route hoặc query.*

### 2.2 Request Body Record DTO
- **Vị trí file:** `Notification.Application/Services/Notifications/NotificationRequests.cs`
- **Tên C# Record:** `RegisterDeviceRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record RegisterDeviceRequest(
    [property: Required(ErrorMessage = "Device token is required.")]
    string Token,
    string Platform = "WEB"
);
```
- **JSON Request Body mẫu (camelCase):**
```json
{
  "token": "fcm_device_token_sample_123456",
  "platform": "WEB"
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** NotificationService CHỈ trả về DTO thuần, KHÔNG bọc qua `ApiResponse<T>`. Vỏ `ApiResponse<T>` do API Gateway tự động đóng gói tại biên.

- **Vị trí file:** `Notification.Application/Services/Notifications/NotificationResponses.cs`
- **Tên C# Record:** `DeviceRegisteredResponse`
- **Mã HTTP thành công:** `200 OK`
- **Khai báo C# Record:**
```csharp
public sealed record DeviceRegisteredResponse(
    string Platform,
    string Token,
    DateTimeOffset RegisteredAt
);
```
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "platform": "WEB",
  "token": "fcm_device_token_sample_123456",
  "registeredAt": "2026-11-02T02:30:00Z"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)
> **Quy chuẩn:** 100% tiếng Anh, Sentence case cho `message`, SCREAMING_SNAKE_CASE cho `errorCode` (không dùng tiền tố `ERR_`).

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `One or more validation errors occurred.` | Thiếu `Token` hoặc rỗng |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Không tìm thấy hoặc thiếu header `x-user-id` |
| `500 Internal Error` | `INTERNAL_SERVER_ERROR` | `An unexpected error occurred while processing the request.` | Lỗi cơ sở dữ liệu hoặc ngoại lệ chưa xử lý |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)
### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Trích xuất `userId` từ HttpContext qua extension `this.TryGetUserId()`. Nếu thất bại trả về `401 Unauthorized`.
2. **Bước 2:** Controller gọi `_notificationService.RegisterDeviceAsync(userId, request, cancellationToken)`.
3. **Bước 3:** Service lưu hoặc cập nhật thiết bị vào bảng `UserDevices` trong `AppDbContext`.
4. **Bước 4:** Trả về `DeviceRegisteredResponse` với mã HTTP `200 OK`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong NotificationService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được lưu tại `docs/api/Notifications/POST_DEVICES.md`.
- [x] **Persistence:** Thực thể `UserDevice` trong `Notification.Domain` đã cấu hình.
- [x] **Application:** `IUserNotificationService` và `NotificationService` đã hoàn tất.
- [x] **Api:** `NotificationsController` kế thừa `ControllerBase`, trả Pure DTO.
- [x] **Tests:** Đã kiểm thử tự động đạt 100% pass.
- [x] **Verify:** `dotnet build` và `dotnet test` vượt qua 100%.

## Device lifecycle update (2026-11-08)
Current authoritative contract: docs/api/Devices/POST_DEVICES.md. Register/refresh/rotation and revoke share per-token transaction locks. Active foreign token409, inactive token can transfer after revoke; deleted token not revived. previousToken enables atomic rotation. Revoke never bypasses ownership, no-op204 if foreign/unknown/inactive; audit fingerprints only. Existing path-token values must be URL-encoded.237 tests pass, no project DB/provider changes or migration.
