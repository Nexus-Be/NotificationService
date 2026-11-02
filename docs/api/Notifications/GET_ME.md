# NOTIFICATION SERVICE - GET /api/v1/notifications/me

> **Feature / Use Case:** Notifications / Get Personal Notifications History
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-02
> **Tác giả:** AI Coding Agent

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `GET`
- **Route Endpoint:** `/api/v1/notifications/me`
- **Quyền hạn (RBAC Policy):** `User` / `Buyer` / `Seller` / `Admin`
- **Quy tắc Sở hữu (Resource Ownership):** `currentUserId == targetUserId`. Người dùng chỉ xem thông báo gửi cho chính mình.
- **Controller:** `Notification.Api/Controllers/Public/NotificationsController.cs` -> `GetMyNotifications()`
- **Application Contract:** `Notification.Application/Services/Notifications/IUserNotificationService.cs` -> `GetMyNotificationsAsync()`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `isRead` | Query | `bool?` | Không | `true` hoặc `false` | Lọc theo trạng thái đã đọc |
| `notificationType` | Query | `string?` | Không | Chuỗi loại thông báo | Lọc theo loại thông báo |
| `page` | Query | `int` | Không | Min: 1, Default: 1 | Trang hiện tại |
| `pageSize` | Query | `int` | Không | Range: 1 - 100, Default: 20 | Số lượng bản ghi mỗi trang |

### 2.2 Request Body Record DTO
*Không có request body (HTTP GET).*

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** NotificationService CHỈ trả về cấu trúc phân trang chuẩn `PagedData<T>` thuần, KHÔNG bọc qua `ApiResponse<T>`.

- **Vị trí file:** `Notification.Application/Services/Notifications/NotificationResponses.cs` & `Notification.Application/Common/Models/PagedData.cs`
- **Tên C# Record:** `PagedData<InAppNotificationItemResponse>`
- **Mã HTTP thành công:** `200 OK`
- **Khai báo C# Record:**
```csharp
public sealed record InAppNotificationItemResponse(
    Guid Id,
    Guid UserId,
    string? Title,
    string? Body,
    string? Payload,
    bool IsRead,
    DateTimeOffset? ReadAt,
    string? NotificationType,
    string Status,
    DateTimeOffset CreatedAt
);
```
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "userId": "11111111-1111-1111-1111-111111111111",
      "title": "Order Confirmed",
      "body": "Your order #99283 has been confirmed.",
      "payload": "{\"orderId\":\"99283\"}",
      "isRead": false,
      "readAt": null,
      "notificationType": "OrderUpdates",
      "status": "Delivered",
      "createdAt": "2026-11-02T02:00:00Z"
    }
  ],
  "pagination": {
    "pageIndex": 1,
    "pageSize": 20,
    "totalCount": 1,
    "totalPages": 1,
    "hasPreviousPage": false,
    "hasNextPage": false
  }
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
2. **Bước 2:** Chuẩn hóa `page` (min: 1) và `pageSize` (1-100).
3. **Bước 3:** Gọi `_notificationService.GetMyNotificationsAsync(userId, request, cancellationToken)`.
4. **Bước 4:** Service truy vấn phân trang SQL qua `Skip/Take` và đếm tổng qua `CountAsync()`.
5. **Bước 5:** Đóng gói và trả về `PagedData<InAppNotificationItemResponse>` (`200 OK`).

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong NotificationService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Lưu tại `docs/api/Notifications/GET_ME.md`.
- [x] **Pagination:** Phân trang thực hiện trực tiếp tại Database qua `Skip` và `Take`.
- [x] **Api:** `NotificationsController.GetMyNotifications` trả Pure DTO.
- [x] **Tests:** Đã kiểm thử tự động đạt 100% pass.
- [x] **Verify:** `dotnet build` và `dotnet test` vượt qua 100%.
