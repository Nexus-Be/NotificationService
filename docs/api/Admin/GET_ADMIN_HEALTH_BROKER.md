# NOTIFICATION SERVICE - GET /api/v1/admin/health/broker

> **Feature / Use Case:** Admin Health / Get Broker Health Status
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-02
> **Tác giả:** AI Coding Agent

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `GET`
- **Route Endpoint:** `/api/v1/admin/health/broker`
- **Quyền hạn (RBAC Policy):** `admin`, `staff`
- **Quy tắc Sở hữu (Resource Ownership):** Dành riêng cho quản trị viên và nhân viên kiểm tra sức khỏe hàng đợi / broker.
- **Controller:** `Notification.Api/Controllers/Public/AdminHealthController.cs` -> `GetBrokerHealth()`
- **Application Contract:** `Notification.Application/Services/Notifications/IUserNotificationService.cs` -> `GetBrokerHealthAsync()`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
*Không có tham số bắt buộc.*

### 2.2 Request Body Record DTO
*Không có request body (HTTP GET).*

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** NotificationService CHỈ trả về DTO thuần `IReadOnlyList<BrokerHealthCheckResponse>`, KHÔNG bọc qua `ApiResponse<T>`.

- **Vị trí file:** `Notification.Application/Services/Notifications/NotificationResponses.cs`
- **Tên C# Record:** `BrokerHealthCheckResponse`
- **Mã HTTP thành công:** `200 OK`
- **Khai báo C# Record:**
```csharp
public sealed record BrokerHealthCheckResponse(
    Guid Id,
    string BrokerName,
    string Status,
    long LatencyMs,
    int QueueDepth,
    string? Detail,
    DateTimeOffset CheckedAt
);
```
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "brokerName": "rabbitmq",
    "status": "Up",
    "latencyMs": 15,
    "queueDepth": 0,
    "detail": "Queue connection responsive",
    "checkedAt": "2026-11-02T02:00:00Z"
  }
]
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
2. **Bước 2:** Controller gọi `IUserNotificationService.GetBrokerHealthAsync(50, cancellationToken)`.
3. **Bước 3:** Service truy vấn bảng `BrokerHealthChecks` sắp xếp theo `CheckedAt DESC` lấy tối đa 50 bản ghi.
4. **Bước 4:** Trả về danh sách `IReadOnlyList<BrokerHealthCheckResponse>` với HTTP `200 OK`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong NotificationService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu lưu tại `docs/api/Admin/GET_ADMIN_HEALTH_BROKER.md`.
- [x] **Api:** `AdminHealthController.GetBrokerHealth` trả Pure DTO.
- [x] **Tests:** Đã kiểm thử tự động đạt 100% pass qua `AdminControllersTests.cs` và `UserNotificationServiceTests.cs`.
- [x] **Verify:** `dotnet build` và `dotnet test` vượt qua 100%.
