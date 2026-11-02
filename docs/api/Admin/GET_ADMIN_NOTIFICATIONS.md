# NOTIFICATION SERVICE - GET /api/v1/admin/notifications

> **Feature / Use Case:** Admin Notifications / Search Notification History
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-02
> **Tác giả:** AI Coding Agent

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `GET`
- **Route Endpoint:** `/api/v1/admin/notifications`
- **Quyền hạn (RBAC Policy):** `admin`, `staff`
- **Quy tắc Sở hữu (Resource Ownership):** Dành riêng cho quản trị viên và nhân viên tra cứu lịch sử thông báo hệ thống.
- **Controller:** `Notification.Api/Controllers/Public/AdminNotificationsController.cs` -> `SearchNotificationHistory()`
- **Application Contract:** `Notification.Application/Services/Notifications/IUserNotificationService.cs` -> `SearchNotificationHistoryAsync()`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `userId` | Query | `Guid?` | Không | UUIDv4 format | Lọc theo người nhận |
| `notificationType` | Query | `string?` | Không | Tên loại thông báo enum | `OrderPaid`, `OrderShipped`, `AuctionOutbid`... |
| `status` | Query | `string?` | Không | Trạng thái gửi | `Pending`, `Completed`, `Partial`, `Failed` |
| `page` | Query | `int` | Không | Min: 1, Default: 1 | Số trang phân trang |
| `pageSize` | Query | `int` | Không | Range: 1 - 100, Default: 20 | Số bản ghi mỗi trang |

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
      "title": "Payment Successful",
      "body": "Your order #1002 has been paid.",
      "payload": "{\"orderId\":\"1002\"}",
      "isRead": true,
      "readAt": "2026-11-02T02:05:00Z",
      "notificationType": "OrderPaid",
      "status": "Completed",
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
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Không có hoặc thiếu header xác thực |
| `403 Forbidden` | `FORBIDDEN` | `You do not have permission to access or modify this resource.` | Người dùng không mang vai trò `admin` hoặc `staff` |
| `500 Internal Error` | `INTERNAL_SERVER_ERROR` | `An unexpected error occurred while processing the request.` | Lỗi cơ sở dữ liệu hoặc ngoại lệ chưa xử lý |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)
### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** `RequireRoleAttribute` xác thực vai trò `admin` hoặc `staff`.
2. **Bước 2:** Chuẩn hóa `page` (min 1) và `pageSize` (1-100).
3. **Bước 3:** Controller ủy quyền cho `IUserNotificationService.SearchNotificationHistoryAsync`.
4. **Bước 4:** Service áp dụng bộ lọc `userId`, `notificationType`, `status`, tính toán phân trang qua `.Skip()` / `.Take()` và đếm tổng qua `.CountAsync()`.
5. **Bước 5:** Trả về `PagedData<InAppNotificationItemResponse>` với HTTP `200 OK`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong NotificationService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu lưu tại `docs/api/Admin/GET_ADMIN_NOTIFICATIONS.md`.
- [x] **Pagination:** Phân trang thực hiện trực tiếp tại Database qua `Skip` và `Take`.
- [x] **Api:** `AdminNotificationsController.SearchNotificationHistory` trả Pure DTO.
- [x] **Tests:** Đã kiểm thử tự động đạt 100% pass qua `AdminControllersTests.cs` và `UserNotificationServiceTests.cs`.
- [x] **Verify:** `dotnet build` và `dotnet test` vượt qua 100%.
