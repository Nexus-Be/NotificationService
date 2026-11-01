---
name: service-api-doc
description: Pre-code API documentation standard for NotificationService (port 8007). Mandates drafting a service-level technical API doc before coding, referencing existing domain entities, enums, DTOs, and error patterns for system-wide consistency. Governs pure DTO contracts, validation attributes, and RFC 9110 error matrices inside NotificationService.
---

# NotificationService API Pre-Code Documentation Skill

> **Scope:** NotificationService repository only (`src/Services/Notification/`). 100% Inward-Only. Never reference the root workspace or sibling services.

This skill governs the **mandatory pre-code documentation lifecycle** within NotificationService. Before any C# code is written or modified for a REST endpoint, the developer or AI agent must create a technical API specification inside `docs/api/`.

---

## 1. Ranh giới Tài liệu NotificationService

* **Vị trí lưu trữ:** `docs/api/{Feature}/{HTTP_METHOD}_{ENDPOINT_SLUG}.md` (ví dụ `docs/api/Notifications/GET_NOTIFICATIONS_ME.md`).
* **Thời điểm lập:** **BẮT BUỘC TRƯỚC KHI VIẾT CODE (Pre-Code)**.
* **Định dạng dữ liệu trả về:** **Pure DTO** hoặc `PagedData<T>` (tuyệt đối không bọc qua `ApiResponse<T>`).
* **Rào chắn Inward-Only:** Chỉ trỏ tới tài nguyên nội bộ của NotificationService (`Notification.Domain`, `Notification.Application`, `Notification.Infrastructure`, `Notification.Api`). Không tham chiếu tới `docs/api/` ở Root.

---

## 2. Quy trình 3 Bước Tra cứu Source of Truth Trước Khi Lập Doc

### Bước 1: Tra cứu Thực thể Domain (`Notification.Domain/Entities/`)
- Mở `Notification.Domain/Entities/` (ví dụ `Notification.cs`, `NotificationPreference.cs`, `NotificationDevice.cs`).
- Ràng buộc: `userId`, channel type, read status, metadata JSON payload.

### Bước 2: Tra cứu Bảng mã Enum (`Notification.Domain/Enums/`)
- Mở `Notification.Domain/Enums/` (ví dụ `NotificationType.cs`, `NotificationChannel.cs`).
- Ghi nhận số nguyên DB (`0`, `1`, `2`...) và chuỗi PascalCase API (`System`, `OrderPaid`, `AuctionOutbid`, `Email`, `InApp`, `Push`).
- Điền bảng ánh xạ vào Mục 4.1 của doc.

### Bước 3: Tra cứu Mẫu DTO & Xử lý Lỗi Có Sẵn (`Notification.Application/`)
- Rà soát các tính năng có sẵn tại `Notification.Application/Services/` (ví dụ `Notifications/`, `Email/`).
- Kế thừa quy ước đặt tên DTO: `{Action}Request`, `{Action}Response`.
- Kế thừa mã lỗi đã có (`NOTIFICATION_NOT_FOUND`, `INVALID_EMAIL`, `VAL_INVALID_INPUT`).

---

## 3. Các Rào Chắn Bất Biến tại NotificationService

1. **Pure DTO:** Controller tại NotificationService trả về DTO thuần, cấm thêm `ResultFilter` hay bọc `ApiResponse<T>`.
2. **Plural Route:** `/api/v1/notifications`, `/api/v1/notifications/devices`, `/api/v1/notifications/preferences`.
3. **Mã lỗi & Message:** 100% tiếng Anh, Sentence case cho `message`, SCREAMING_SNAKE_CASE cho `errorCode` (cấm tiền tố `ERR_`).
4. **Quyền sở hữu thông báo cá nhân:** Người dùng chỉ được xem/sửa thông báo thuộc về `currentUserId`.
5. **Phân trang tại DB:** Toàn bộ danh sách phân trang tính toán tại DB qua `Skip`/`Take` và `CountAsync`, trả về `PagedData<T>`.

---

## 4. Quy trình Thực hiện (Workflow)

1. Sao chép `docs/api/SERVICE_API_DOC_TEMPLATE.md` vào `docs/api/{Feature}/{HTTP_METHOD}_{ENDPOINT_SLUG}.md`.
2. Điền đầy đủ 5 phần của template dựa trên kết quả tra cứu Source of Truth.
3. Hoàn thiện mục 5.1 (Pre-Code Plan) mô tả các bước xử lý kỹ thuật.
4. Triển khai code C# tuân thủ 100% hợp đồng đã đặc tả trong doc.
5. Chạy `dotnet test Notification.sln -c Release` (hoặc build `Notification.sln`) đạt 100% pass, tích checklist và chuyển trạng thái doc sang `✅ VERIFIED`.
