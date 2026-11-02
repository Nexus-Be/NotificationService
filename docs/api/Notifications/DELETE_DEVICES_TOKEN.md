# NOTIFICATION SERVICE - DELETE /api/v1/notifications/devices/{token}

> **Feature / Use Case:** Notifications / Revoke Device Token
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-02
> **Tác giả:** AI Coding Agent

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `DELETE`
- **Route Endpoint:** `/api/v1/notifications/devices/{token}`
- **Quyền hạn (RBAC Policy):** Mọi người dùng đã xác thực (`User` / `Buyer` / `Seller` / `Admin`)
- **Quy tắc Sở hữu (Resource Ownership):** Hủy kích hoạt token thiết bị đã đăng ký khi đăng xuất.
- **Controller:** `Notification.Api/Controllers/Public/NotificationsController.cs` -> `RevokeDevice()`
- **Application Contract:** `Notification.Application/Services/Notifications/IUserNotificationService.cs` -> `RevokeDeviceAsync()`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `token` | Route | `string` | Có | Không rỗng | Token thiết bị cần hủy |

### 2.2 Request Body Record DTO
*Không có request body (HTTP DELETE).*

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** Endpoint hủy/xóa thành công trả về `204 NoContent` không có nội dung body.

- **Mã HTTP thành công:** `204 NoContent`
- **Body:** Rỗng

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)
> **Quy chuẩn:** 100% tiếng Anh, Sentence case cho `message`, SCREAMING_SNAKE_CASE cho `errorCode` (không dùng tiền tố `ERR_`).

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Thiếu hoặc token không hợp lệ |
| `500 Internal Error` | `INTERNAL_SERVER_ERROR` | `An unexpected error occurred while processing the request.` | Lỗi cơ sở dữ liệu hoặc ngoại lệ chưa xử lý |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)
### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Trích xuất `token` từ route parameter.
2. **Bước 2:** Controller gọi `_notificationService.RevokeDeviceAsync(token, cancellationToken)`.
3. **Bước 3:** Service xóa hoặc đánh dấu không hoạt động cho token trong DB.
4. **Bước 4:** Trả về HTTP `204 NoContent`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong NotificationService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được lưu tại `docs/api/Notifications/DELETE_DEVICES_TOKEN.md`.
- [x] **Api:** `NotificationsController.RevokeDevice` trả về HTTP `204 NoContent`.
- [x] **Tests:** Đã kiểm thử tự động đạt 100% pass.
- [x] **Verify:** `dotnet build` và `dotnet test` vượt qua 100%.
