<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **NotificationService** (455 symbols, 581 relationships, 4 execution flows).

> Index stale? Run `node .gitnexus/run.cjs analyze --index-only` from the project root.

## Always Do

- **MUST run impact before editing.** Use `impact({target: "symbolName", direction: "upstream"})` or `node .gitnexus/run.cjs impact "symbolName" --direction upstream --repo .`.
- **MUST analyze graph changes before committing.** Use `detect_changes({scope: "all"})` or `node .gitnexus/run.cjs detect-changes --scope all --repo .`.
- **MUST warn on HIGH/CRITICAL `risk` pre-edit.**
- **MUST treat `risk: UNKNOWN` as unresolved, not low.**
- **NEVER** edit before impact analysis · **NEVER** rename with find-and-replace · **NEVER** commit before detect_changes.

<!-- gitnexus:end -->

---

# QUY CHUẨN VẬN HÀNH DÀNH CHO AI CODING AGENTS (NOTIFICATION SERVICE)

> **Trạng thái triển khai (2026-10-30):** ✅ ĐÃ CHUẨN HÓA — 100% RESTful API (`NotificationsController`, `EmailController`), Feature Co-location, cấm gRPC, bãi bỏ hoàn toàn RabbitMQ, MassTransit và Outbox/Inbox tables.
> **Nguồn sự thật:** code (`Notification.sln`, `Notification.Api/Controllers/NotificationsController.cs`, `Notification.Api/Controllers/EmailController.cs`).
> **Cập nhật lần cuối:** 2026-10-30.

---

## 🛑 NGUYÊN TẮC 1: BẮT BUỘC LẬP KẾ HOẠCH TRƯỚC KHI HÀNH ĐỘNG (PLAN-FIRST RULE)

Mọi AI Agent khi nhận nhiệm vụ kỹ thuật tại Notification **BẮT BUỘC PHẢI LẬP VÀ TRÌNH BÀY PLAN** trước khi thực hiện bất kỳ hành động sửa đổi mã nguồn, thêm/xóa file, refactor, tạo migration database hay thay đổi cấu hình nào.

Một Plan chuẩn bắt buộc phải có:
1. **Mục tiêu (Objective)**: Tóm tắt chính xác điều cần làm.
2. **Context & Files cần đọc**: Liệt kê các file nội bộ của Notification theo ma trận đọc file tại Nguyên tắc 3.
3. **Phân tích rủi ro & Phạm vi ảnh hưởng (Impact Analysis)**: Dùng GitNexus `impact`.
4. **Checklist các bước thực hiện (Step-by-Step Implementation)**.
5. **Kế hoạch kiểm chứng (Verification)**: `dotnet build Notification.sln -c Release` và `dotnet test Notification.sln -c Release`.

---

## 🧭 NGUYÊN TẮC 2: QUY TẮC PHẠM VI NỘI BỘ (INWARD-ONLY / SELF-CONTAINED)

Notification là một Git repository độc lập:
* **TUYỆT ĐỐI CHỈ ĐƯỢC trỏ tới các tài nguyên nội bộ của Notification**.
* **TUYỆT ĐỐI KHÔNG ĐƯỢC trỏ ngược ra ngoài repo** hay sang service khác.
* Toàn bộ tài liệu, hướng dẫn và cấu hình trong repo này phải 100% tự cung tự cấp (Self-contained).

---

## 📚 NGUYÊN TẮC 3: DANH MỤC FILE NỘI BỘ BẮT BUỘC PHẢI ĐỌC TẠI NOTIFICATION

Khi bắt đầu làm việc tại Notification, AI Agent **BẮT BUỘC PHẢI ĐỌC** các file nội bộ sau theo thứ tự:
1. `specs/00_AGENT_MISSION_GUARDRAILS.md`: Bounded context thông báo in-app, email SMTP/OTP, port `8007`, DB `5439` (`notification_db`).
2. `specs/01_CLEAN_ARCHITECTURE_LAYERS.md`: 4 tầng Clean Architecture, No-Repository pattern over EF Core.
3. `specs/02_API_REST_CONTRACTS.md`: Hợp đồng RESTful API: `NotificationsController`, `EmailController`.
4. `specs/03_EVENT_MESSAGING.md`: Trạng thái bãi bỏ RabbitMQ / MassTransit; gửi email qua REST API đồng bộ.
5. `specs/04_DATA_PERSISTENCE.md`: Schema PostgreSQL (`Notifications`, `NotificationPreferences`...).
6. `specs/05_DEV_VERIFICATION_CHECKLIST.md`: Lệnh build & test kiểm chứng service.

---

## 🔒 NGUYÊN TẮC 4: RÀO CHẮN BẤT BIẾN TẠI NOTIFICATION

1. **TUYỆT ĐỐI KHÔNG DÙNG REPOSITORY PATTERN BỌC NGOÀI EF CORE**: Dùng trực tiếp `AppDbContext` qua interface `INotificationDbContext` và `DbSet<T>`. Cấm tạo `INotificationRepository`, `UnitOfWork`.
2. **CẤU HÌNH PORT 8007 VÀ DATABASE 5439 LÀ BẤT BIẾN**.
3. **CẤM SỬ DỤNG gRPC VÀ BÃI BỎ MESSAGE BROKER — BẮT BUỘC DÙNG 100% RESTFUL API**:
   - Toàn bộ giao tiếp đồng bộ sử dụng chuẩn **RESTful Web API** (JSON qua HTTP/1.1 hoặc HTTP/2).
   - Tuyệt đối cấm gRPC, Protobuf, RabbitMQ và MassTransit.
4. **FEATURE CO-LOCATION & 1-TO-1 CONTROLLER PAIRING**:
   - Tầng Application: Toàn bộ use cases đặt tại `Notification.Application/Services/{Feature}/` gồm `I{Feature}Service.cs`, `{Feature}Service.cs`, `{Feature}Requests.cs`, `{Feature}Responses.cs`, `{Feature}Dtos.cs`.
   - Tầng Api: Ghép cặp 1-to-1 với `NotificationsController.cs` và `EmailController.cs`, chỉ inject duy nhất feature service tương ứng.

---

## 💬 NGUYÊN TẮC 5: QUY CHUẨN THÔNG BÁO LỖI TOÀN HỆ THỐNG

1. **Ngôn ngữ (100% English)**: Toàn bộ thông báo lỗi viết bằng tiếng Anh. Tuyệt đối không dùng tiếng Việt.
2. **Quy cách viết hoa (Sentence case)**: Viết hoa duy nhất chữ cái đầu câu: `"Notification not found or does not belong to the user."`
3. **Mã lỗi (SCREAMING_SNAKE_CASE)**: `NOTIFICATION_NOT_FOUND`, `INVALID_EMAIL`, `VAL_INVALID_INPUT`, `UNAUTHORIZED`, `FORBIDDEN`.

---

## 🎯 NGUYÊN TẮC 6: ĐIỀU KIỆN HOÀN THÀNH NHIỆM VỤ (DEFINITION OF DONE)

Một task tại Notification chỉ hoàn tất khi:
* Đã lập Plan chi tiết trước khi thực hiện.
* Không chứa bất kỳ liên kết trỏ ngược ra ngoài Notification.
* `dotnet build Notification.sln -c Release` thành công 100% (0 error).
* `dotnet test Notification.sln -c Release` vượt qua 100% các bài test.
* Trạng thái Git sạch, không commit file rác (`bin/`, `obj/`, `.vs/`, `.idea/`).
