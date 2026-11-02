# NOTIFICATION SERVICE - PATCH /api/v1/notifications/me/read

> **Feature / Use Case:** Notifications / Batch Mark Notifications as Read
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-02
> **Tác giả:** AI Coding Agent

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `PATCH`
- **Route Endpoint:** `/api/v1/notifications/me/read`
- **Quyền hạn (RBAC Policy):** `User` / `Buyer` / `Seller` / `Admin`
- **Quy tắc Sở hữu (Resource Ownership):** `currentUserId == targetUserId`. Chỉ cập nhật trạng thái các thông báo thuộc sở hữu của người dùng.
- **Controller:** `Notification.Api/Controllers/Public/NotificationsController.cs` -> `MarkNotificationsAsRead()`
- **Application Contract:** `Notification.Application/Services/Notifications/IUserNotificationService.cs` -> `MarkNotificationsAsReadAsync()`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
*Không có tham số route hoặc query.*

### 2.2 Request Body Record DTO
- **Vị trí file:** `Notification.Application/Services/Notifications/NotificationRequests.cs`
- **Tên C# Record:** `BatchMarkReadRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record BatchMarkReadRequest(
    IReadOnlyList<Guid>? NotificationIds = null
);
```
> **Ghi chú:** Nếu `NotificationIds` rỗng hoặc `null`, hệ thống sẽ đánh dấu toàn bộ thông báo chưa đọc của user thành đã đọc.

- **JSON Request Body mẫu (camelCase):**
```json
{
  "notificationIds": [
    "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "7ca85f64-5717-4562-b3fc-2c963f66afb2"
  ]
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** NotificationService CHỈ trả về DTO thuần, KHÔNG bọc qua `ApiResponse<T>`.

- **Vị trí file:** `Notification.Application/Services/Notifications/NotificationResponses.cs`
- **Tên C# Record:** `BatchMarkReadResult`
- **Mã HTTP thành công:** `200 OK`
- **Khai báo C# Record:**
```csharp
public sealed record BatchMarkReadResult(
    int MarkedCount
);
```
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "markedCount": 2
}
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
2. **Bước 2:** Gọi `_notificationService.MarkNotificationsAsReadAsync(userId, request, cancellationToken)`.
3. **Bước 3:** Service lọc các thông báo của `userId` chưa đọc (khớp ID nếu có chỉ định, hoặc toàn bộ nếu null), gán `IsRead = true`, `ReadAt = DateTimeOffset.UtcNow`.
4. **Bước 4:** Commit transaction và trả về `BatchMarkReadResult` với số lượng bản ghi đã đánh dấu (`200 OK`).

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong NotificationService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Lưu tại `docs/api/Notifications/PATCH_ME_READ.md`.
- [x] **Route Convention:** Chuẩn hóa phương thức HTTP là `PATCH` đúng ngữ nghĩa RESTful.
- [x] **Api:** `NotificationsController.MarkNotificationsAsRead` trả Pure DTO.
- [x] **Tests:** Đã kiểm thử tự động đạt 100% pass.
- [x] **Verify:** `dotnet build` và `dotnet test` vượt qua 100%.
