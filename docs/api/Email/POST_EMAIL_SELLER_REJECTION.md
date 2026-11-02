# NOTIFICATION SERVICE - POST /api/v1/notifications/email/seller-rejection

> **Feature / Use Case:** Email / Send Seller Application Rejection Email
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-02
> **Tác giả:** AI Coding Agent

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/notifications/email/seller-rejection`
- **Quyền hạn (RBAC Policy):** Liên dịch vụ / Admin / Staff (gọi từ `UserService` trong Flow 2 - Step 4B khi Admin từ chối đơn đăng ký người bán)
- **Quy tắc Sở hữu (Resource Ownership):** Gửi email thông báo từ chối kèm lý do chi tiết cho người nộp đơn.
- **Controller:** `Notification.Api/Controllers/Public/EmailController.cs` -> `SendSellerRejectionEmail()`
- **Application Contract:** `Notification.Application/Services/Email/IEmailNotificationService.cs` -> `SendEmailAsync()`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
*Không có tham số route hoặc query.*

### 2.2 Request Body Record DTO
- **Vị trí file:** `Notification.Api/Controllers/Public/EmailController.cs`
- **Tên C# Record:** `SendSellerRejectionEmailApiRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record SendSellerRejectionEmailApiRequest
{
    [Required]
    public string Email { get; init; } = string.Empty;

    public string? FullName { get; init; }

    public string? ShopName { get; init; }

    [Required]
    public string RejectReason { get; init; } = string.Empty;
}
```
- **JSON Request Body mẫu (camelCase):**
```json
{
  "email": "seller@example.com",
  "fullName": "Nguyen Van B",
  "shopName": "My Shop",
  "rejectReason": "Incomplete business license documents."
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** NotificationService trả về payload JSON trực tiếp.

- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (camelCase):**
```json
{
  "success": true,
  "message": "Seller rejection email dispatched successfully.",
  "data": {
    "recipient": "seller@example.com",
    "shopName": "My Shop",
    "rejectReason": "Incomplete business license documents.",
    "dispatchedAt": "2026-11-02T02:30:00Z"
  }
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)
> **Quy chuẩn:** 100% tiếng Anh, Sentence case cho `message`, SCREAMING_SNAKE_CASE cho `errorCode` (không dùng tiền tố `ERR_`).

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_EMAIL` | `Recipient email is required and must be a valid email address.` | `Email` rỗng, thiếu hoặc không chứa `@` |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `Rejection reason is required.` | `RejectReason` rỗng hoặc null |
| `500 Internal Error` | `EMAIL_SEND_FAILED` | `Failed to deliver seller rejection email via notification provider.` | SMTP provider gặp lỗi không gửi được mail |
| `500 Internal Error` | `INTERNAL_SERVER_ERROR` | `An unexpected error occurred while processing the request.` | Ngoại lệ chưa được xử lý |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)
### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Validate `request.Email` và `request.RejectReason`.
2. **Bước 2:** HTML encode các trường thông tin đầu vào (`fullName`, `shopName`, `rejectReason`) để phòng ngừa XSS Injection.
3. **Bước 3:** Tạo tiêu đề và nội dung HTML thông báo từ chối chuyên nghiệp.
4. **Bước 4:** Gửi email qua `_emailNotificationService.SendEmailAsync()`.
5. **Bước 5:** Trả về `200 OK` kèm dữ liệu xác nhận.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong NotificationService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Lưu tại `docs/api/Email/POST_EMAIL_SELLER_REJECTION.md`.
- [x] **Public Route:** Đạt chuẩn route công khai không dùng prefix `/internal`.
- [x] **Api:** `EmailController.SendSellerRejectionEmail` hoạt động chính xác.
- [x] **Tests:** Đã kiểm thử tự động đạt 100% pass.
- [x] **Verify:** `dotnet build` và `dotnet test` vượt qua 100%.
