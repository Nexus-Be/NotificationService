# NOTIFICATION SERVICE - GET /api/v1/notifications/preferences

> **Feature / Use Case:** Notifications / Get User Notification Preferences
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-02
> **Tác giả:** AI Coding Agent

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `GET`
- **Route Endpoint:** `/api/v1/notifications/preferences`
- **Quyền hạn (RBAC Policy):** `User` / `Buyer` / `Seller` / `Admin`
- **Quy tắc Sở hữu (Resource Ownership):** `currentUserId == targetUserId`. Người dùng chỉ đọc tùy chọn thông báo của chính mình.
- **Controller:** `Notification.Api/Controllers/Public/NotificationsController.cs` -> `GetMyPreferences()`
- **Application Contract:** `Notification.Application/Services/Notifications/IUserNotificationService.cs` -> `GetPreferencesAsync()`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
*Không có tham số route hoặc query.*

### 2.2 Request Body Record DTO
*Không có request body (HTTP GET).*

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** NotificationService CHỈ trả về DTO thuần, KHÔNG bọc qua `ApiResponse<T>`.

- **Vị trí file:** `Notification.Application/Services/Notifications/NotificationResponses.cs`
- **Tên C# Record:** `IReadOnlyList<NotificationPreferenceResponse>`
- **Mã HTTP thành công:** `200 OK`
- **Khai báo C# Record:**
```csharp
public sealed record NotificationPreferenceResponse(
    Guid Id,
    string NotificationType,
    string ChannelCode,
    bool IsDisabled
);
```
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "notificationType": "OrderUpdates",
    "channelCode": "EMAIL",
    "isDisabled": false
  },
  {
    "id": "7ca85f64-5717-4562-b3fc-2c963f66afb2",
    "notificationType": "Promotions",
    "channelCode": "PUSH",
    "isDisabled": true
  }
]
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)
> **Quy chuẩn:** 100% tiếng Anh, Sentence case cho `message`, SCREAMING_SNAKE_CASE cho `errorCode` (không dùng tiền tố `ERR_`).

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Không tìm thấy hoặc thiếu header `x-user-id` |
| `500 Internal Error` | `INTERNAL_SERVER_ERROR` | `An unexpected error occurred while processing the request.` | Ngoại lệ chưa được xử lý |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)
### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Trích xuất `userId` từ HttpContext.
2. **Bước 2:** Gọi `_notificationService.GetPreferencesAsync(userId, cancellationToken)`.
3. **Bước 3:** Service truy vấn bảng `NotificationPreferences` theo `UserId`. Nếu chưa có cấu hình, khởi tạo tùy chọn mặc định.
4. **Bước 4:** Trả về danh sách `NotificationPreferenceResponse` với mã HTTP `200 OK`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong NotificationService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Lưu tại `docs/api/Notifications/GET_PREFERENCES.md`.
- [x] **Api:** `NotificationsController.GetMyPreferences` trả Pure DTO.
- [x] **Tests:** Đã kiểm thử tự động đạt 100% pass.
- [x] **Verify:** `dotnet build` và `dotnet test` vượt qua 100%.
