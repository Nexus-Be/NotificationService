# 04 — MÔ HÌNH THỰC THỂ & CƠ SỞ DỮ LIỆU (NOTIFICATION SERVICE DATA PERSISTENCE)

---

## 1. MÔ HÌNH THỰC THỂ CHÍNH (DOMAIN ENTITIES)

1. **`Notification`**:
   - `Id`: `Guid`.
   - `UserId`: `Guid` (Người nhận thông báo).
   - `Title`: `string` (Tiêu đề thông báo, Max 200 chars).
   - `Content`: `string` (Nội dung chi tiết).
   - `Type`: `string` (`"Order"`, `"Auction"`, `"System"`, `"Promotion"`).
   - `IsRead`: `bool` (Mặc định: `false`).
   - `ActionUrl`: `string?` (Đường dẫn deep link mở chi tiết đơn/đấu giá trên app).
   - `CreatedAt`: `DateTimeOffset`.

2. **`NotificationPreference`**:
   - `Id`: `Guid`.
   - `UserId`: `Guid` (Unique, mỗi user có 1 bản ghi cấu hình).
   - `EmailEnabled`: `bool` (Mặc định: `true`).
   - `PushEnabled`: `bool` (Mặc định: `true`).
   - `OrderUpdates`: `bool` (Mặc định: `true`).
   - `AuctionAlerts`: `bool` (Mặc định: `true`).
   - `UpdatedAt`: `DateTimeOffset`.

---

## 2. QUY CHUẨN CƠ SỞ DỮ LIỆU & EF CORE

- Engine: PostgreSQL.
- Database: `notification_db` tại port `5438`.
- Chuỗi kết nối: `Host=localhost;Port=5438;Database=notification_db;Username=postgres;Password=...`.
- Tên bảng: `notifications`, `notification_preferences`.
- **Đánh Index**: `Index` trên `(UserId, IsRead, CreatedAt)` để tối ưu hóa truy vấn thông báo chưa đọc của user.
- **No Repository Pattern**: Tầng Application tương tác trực tiếp với `NotificationDbContext`.
