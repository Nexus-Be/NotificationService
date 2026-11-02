# NOTIFICATION SERVICE - POST /api/v1/internal/notifications/email/otp

> **Feature / Use Case:** Internal Email / Send OTP Verification Email With Purpose
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-02
> **Tác giả:** AI Coding Agent

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/internal/notifications/email/otp`
- **Quyền hạn (RBAC Policy):** Nội bộ / Inter-service (dành cho các service nội bộ cần chỉ định mục đích gửi OTP như `registration`, `password_reset`, `transaction_verify`)
- **Quy tắc Sở hữu (Resource Ownership):** Gửi email chứa mã OTP kèm mục đích (purpose) xác thực tới người nhận.
- **Controller:** `Notification.Api/Controllers/Internal/InternalEmailController.cs` -> `SendOtpEmail()`
- **Application Contract:** `Notification.Application/Services/Email/IEmailNotificationService.cs` -> `SendOtpEmailAsync()`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
*Không có tham số route hoặc query.*

### 2.2 Request Body Record DTO
- **Vị trí file:** `Notification.Api/Controllers/Internal/InternalEmailController.cs`
- **Tên C# Record:** `InternalSendOtpEmailApiRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record InternalSendOtpEmailApiRequest
{
    [Required]
    public string Email { get; init; } = string.Empty;

    public string? FullName { get; init; }

    [Required]
    public string OtpCode { get; init; } = string.Empty;

    public int ExpiresInMinutes { get; init; } = 5;

    public string? Purpose { get; init; } = "registration";
}
```
- **JSON Request Body mẫu (camelCase):**
```json
{
  "email": "buyer@example.com",
  "fullName": "Nguyen Van A",
  "otpCode": "123456",
  "expiresInMinutes": 5,
  "purpose": "password_reset"
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** NotificationService trả về payload JSON trực tiếp biểu thị kết quả gửi email.

- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (camelCase):**
```json
{
  "success": true,
  "message": "OTP verification email dispatched successfully.",
  "data": {
    "recipient": "buyer@example.com",
    "fullName": "Nguyen Van A",
    "expiresInMinutes": 5,
    "purpose": "password_reset",
    "dispatchedAt": "2026-11-02T02:30:00Z"
  }
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)
> **Quy chuẩn:** 100% tiếng Anh, Sentence case cho `message`, SCREAMING_SNAKE_CASE cho `errorCode` (không dùng tiền tố `ERR_`).

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_EMAIL` | `Recipient email is required and must be a valid email address.` | `Email` rỗng, thiếu hoặc không chứa ký tự `@` |
| `400 Bad Request` | `VAL_INVALID_OTP` | `OTP code cannot be empty.` | `OtpCode` rỗng hoặc null |
| `500 Internal Error` | `EMAIL_SEND_FAILED` | `Failed to deliver OTP email via notification provider.` | SMTP provider gặp lỗi không gửi được mail |
| `500 Internal Error` | `INTERNAL_SERVER_ERROR` | `An unexpected error occurred while processing the request.` | Ngoại lệ chưa được xử lý |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)
### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Validate `request.Email` (phải chứa `@`) và `request.OtpCode` (không được rỗng).
2. **Bước 2:** Chuẩn hóa `fullName` (default "Customer"), `purpose` (default "registration"), và `expiresInMinutes` (default 5).
3. **Bước 3:** Chuyển đổi sang `SendOtpEmailDto` và gọi `_emailNotificationService.SendOtpEmailAsync()`.
4. **Bước 4:** Nếu gửi thất bại, log warning và trả `500 EMAIL_SEND_FAILED`.
5. **Bước 5:** Nếu thành công, trả `200 OK` kèm thông tin dispatched (bao gồm trường `purpose`).

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong NotificationService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Lưu tại `docs/api/Email/POST_INTERNAL_EMAIL_OTP.md`.
- [x] **Internal Route:** Định tuyến riêng biệt `/api/v1/internal/notifications/email/otp` không xung đột với Public controller.
- [x] **Tests:** Kiểm thử tự động bao phủ trong `InternalEmailControllerTests.cs`.
