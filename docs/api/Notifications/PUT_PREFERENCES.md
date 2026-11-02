# NOTIFICATION SERVICE - PUT /api/v1/notifications/preferences

> **Feature / Use Case:** Notifications / Update User Notification Preferences
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-02
> **Tác giả:** AI Coding Agent

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `PUT`
- **Route Endpoint:** `/api/v1/notifications/preferences`
- **Quyền hạn (RBAC Policy):** `User` / `Buyer` / `Seller` / `Admin`
- **Quy tắc Sở hữu (Resource Ownership):** `currentUserId == targetUserId`. Người dùng chỉ cập nhật tùy chọn của chính mình.
- **Controller:** `Notification.Api/Controllers/Public/NotificationsController.cs` -> `UpdatePreferences()`
- **Application Contract:** `Notification.Application/Services/Notifications/IUserNotificationService.cs` -> `UpdatePreferencesAsync()`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
*Không có tham số route hoặc query.*

### 2.2 Request Body Record DTO
- **Vị trí file:** `Notification.Application/Services/Notifications/NotificationRequests.cs`
- **Tên C# Record:** `UpdatePreferencesRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record UpdatePreferenceItemRequest(
    string NotificationType,
    string ChannelCode,
    bool IsDisabled
);

public sealed record UpdatePreferencesRequest(
    IReadOnlyList<UpdatePreferenceItemRequest> Preferences
);
```
- **JSON Request Body mẫu (camelCase):**
```json
{
  "preferences": [
    {
      "notificationType": "Promotions",
      "channelCode": "EMAIL",
      "isDisabled": true
    },
    {
      "notificationType": "OrderUpdates",
      "channelCode": "PUSH",
      "isDisabled": false
    }
  ]
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** NotificationService CHỈ trả về DTO thuần, KHÔNG bọc qua `ApiResponse<T>`.

- **Vị trí file:** `Notification.Application/Services/Notifications/NotificationResponses.cs`
- **Tên C# Record:** `UpdatePreferencesResult`
- **Mã HTTP thành công:** `200 OK`
- **Khai báo C# Record:**
```csharp
public sealed record UpdatePreferencesResult(
    int UpdatedCount
);
```
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "updatedCount": 2
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)
> **Quy chuẩn:** 100% tiếng Anh, Sentence case cho `message`, SCREAMING_SNAKE_CASE cho `errorCode` (không dùng tiền tố `ERR_`).

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `One or more validation errors occurred.` | Payload không hợp lệ hoặc danh sách rỗng |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Không tìm thấy hoặc thiếu header `x-user-id` |
| `500 Internal Error` | `INTERNAL_SERVER_ERROR` | `An unexpected error occurred while processing the request.` | Lỗi cập nhật cơ sở dữ liệu |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)
### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Trích xuất `userId` từ HttpContext.
2. **Bước 2:** Gọi `_notificationService.UpdatePreferencesAsync(userId, request, cancellationToken)`.
3. **Bước 3:** Service lặp qua các mục tùy chọn, cập nhật hoặc thêm mới vào bảng `NotificationPreferences` và commit transaction.
4. **Bước 4:** Trả về `UpdatePreferencesResult` chứa số lượng bản ghi đã cập nhật (`200 OK`).

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong NotificationService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Lưu tại `docs/api/Notifications/PUT_PREFERENCES.md`.
- [x] **Api:** `NotificationsController.UpdatePreferences` trả Pure DTO.
- [x] **Tests:** Đã kiểm thử tự động đạt 100% pass.
- [x] **Verify:** `dotnet build` và `dotnet test` vượt qua 100%.
