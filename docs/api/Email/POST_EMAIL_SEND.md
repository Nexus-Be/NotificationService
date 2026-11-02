# NOTIFICATION SERVICE - POST /api/v1/notifications/email/send

> **Feature / Use Case:** Email / Send General HTML Email
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-02
> **Tác giả:** AI Coding Agent

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/notifications/email/send`
- **Quyền hạn (RBAC Policy):** Công khai / Liên dịch vụ
- **Quy tắc Sở hữu (Resource Ownership):** Gửi email thông báo HTML theo mẫu đến người nhận.
- **Controller:** `Notification.Api/Controllers/Public/EmailController.cs` -> `SendGeneralEmail()`
- **Application Contract:** `Notification.Application/Services/Email/IEmailNotificationService.cs` -> `SendEmailAsync()`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
*Không có tham số route hoặc query.*

### 2.2 Request Body Record DTO
- **Vị trí file:** `Notification.Api/Controllers/Public/EmailController.cs`
- **Tên C# Record:** `SendGeneralEmailApiRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record SendGeneralEmailApiRequest
{
    [Required]
    public string ToEmail { get; init; } = string.Empty;

    [Required]
    public string Subject { get; init; } = string.Empty;

    [Required]
    public string HtmlBody { get; init; } = string.Empty;
}
```
- **JSON Request Body mẫu (camelCase):**
```json
{
  "toEmail": "user@example.com",
  "subject": "Order Confirmation #99283",
  "htmlBody": "<p>Thank you for your purchase.</p>"
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
  "message": "Email dispatched successfully.",
  "data": {
    "recipient": "user@example.com",
    "subject": "Order Confirmation #99283",
    "dispatchedAt": "2026-11-02T02:30:00Z"
  }
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)
> **Quy chuẩn:** 100% tiếng Anh, Sentence case cho `message`, SCREAMING_SNAKE_CASE cho `errorCode` (không dùng tiền tố `ERR_`).

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_EMAIL` | `Recipient email is required and must be a valid email address.` | `ToEmail` rỗng, thiếu hoặc không chứa `@` |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `Email subject is required.` | `Subject` rỗng hoặc null |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `Email HTML content is required.` | `HtmlBody` rỗng hoặc null |
| `500 Internal Error` | `EMAIL_SEND_FAILED` | `Failed to deliver email via notification provider.` | SMTP provider gặp lỗi không gửi được mail |
| `500 Internal Error` | `INTERNAL_SERVER_ERROR` | `An unexpected error occurred while processing the request.` | Ngoại lệ chưa được xử lý |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)
### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Validate `ToEmail`, `Subject`, `HtmlBody`.
2. **Bước 2:** Chuyển đổi sang `SendGeneralEmailDto`.
3. **Bước 3:** Gọi `_emailNotificationService.SendEmailAsync(dto, cancellationToken)`.
4. **Bước 4:** Nếu gửi thất bại, log warning và trả `500 EMAIL_SEND_FAILED`.
5. **Bước 5:** Nếu thành công, trả `200 OK`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong NotificationService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Lưu tại `docs/api/Email/POST_EMAIL_SEND.md`.
- [x] **Public Route:** Đạt chuẩn route công khai không dùng prefix `/internal`.
- [x] **Api:** `EmailController.SendGeneralEmail` hoạt động chính xác.
- [x] **Tests:** Đã kiểm thử tự động đạt 100% pass.
- [x] **Verify:** `dotnet build` và `dotnet test` vượt qua 100%.
