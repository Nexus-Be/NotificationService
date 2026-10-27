# 05 — DANH SÁCH KIỂM CHỨNG & NGHIỆM THU (NOTIFICATION SERVICE CHECKLIST)

> **Trạng thái triển khai (2026-10-27):** ✅ ĐÃ HOÀN TẤT & NGHIỆM THU — Toàn bộ **7/7 test cases** trong `Notification.Tests` PASS 100%, biên dịch Release 0 error. Cả 2 controllers `NotificationsController` và `EmailController` hoạt động độc lập và sẵn sàng tiếp nhận request REST downstream từ ApiGateway hoặc gọi trực tiếp qua port `8007`.
> **Nguồn sự thật:** code (`Notification.Tests/`, `Notification.Api/Controllers/`).
> **Cập nhật lần cuối:** 2026-10-27.

---

## 1. LỆNH BIÊN DỊCH & CHẠY THỬ NGHIỆM

```bash
# 1. Biên dịch solution Notification (5 project)
dotnet build Notification.sln -c Release

# 2. Chạy kiểm thử tự động (7 test cases PASS 100%)
dotnet test Notification.sln -c Release

# 3. Khởi chạy service — HTTP RESTful tại cổng 8007
dotnet run --project Notification.Api
curl -i http://localhost:8007/health
```

**Kết quả kiểm thử thực tế:**

| Bước | Kỳ vọng | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `dotnet build` | 0 error, 5 projects | ✅ ĐẠT | `Notification.sln` |
| `dotnet test` | 7 passed, 0 failed | ✅ ĐẠT | `Notification.Tests/` (Email & Controllers) |
| `GET /health` | 200 `{"status":"Healthy","service":"Notification.Api"}` | ✅ ĐẠT | `Program.cs` |
| `POST /api/v1/notifications/email/otp` | Gửi email OTP thành công | ✅ ĐẠT | `EmailController.cs:20-37` |
| `POST /api/v1/notifications/email/send` | Gửi email thông báo chung | ✅ ĐẠT | `EmailController.cs:39-55` |
| `POST /api/v1/notifications/devices` | Đăng ký thiết bị nhận push | ✅ ĐẠT | `NotificationsController.cs:25-34` |
| `GET /api/v1/notifications/me` | Lấy danh sách thông báo in-app | ✅ ĐẠT | `NotificationsController.cs:64-75` |
| `PUT /api/v1/notifications/me/read` | Đánh dấu thông báo đã đọc | ✅ ĐẠT | `NotificationsController.cs:77-88` |

---

## 2. CHECKLIST TRƯỚC KHI COMMIT (CHO AI AGENTS)

- [x] **Biên dịch sạch**: `dotnet build Notification.sln -c Release` không lỗi (0 error, 0 warning liên quan gRPC).
- [x] **Không vi phạm rào chắn**: không thêm `*Repository.cs`, không `UnitOfWork` bọc EF Core.
- [x] **Cổng bất biến**: service `8007`, DB `5439` (`notification_db`).
- [x] **Giao tiếp đồng bộ chuẩn RESTful**: CẤM gRPC, toàn bộ API tuân thủ REST HTTP/JSON.
- [x] **Git sạch**: không commit `bin/`, `obj/`, `.idea/`, `.env`.
