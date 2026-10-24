# 04 — MÔ HÌNH THỰC THỂ & CƠ SỞ DỮ LIỆU (NOTIFICATION SERVICE DATA PERSISTENCE)

> **Trạng thái triển khai (2026-10-24):** ⚠️ MỘT PHẦN — 10 DbSet ↔ 13 bảng đã tạo bằng migration, nhưng **5 entity chưa từng được ghi** và consumer duy nhất **không inject DbContext**.
> **Nguồn sự thật:** code (`Notification.Infrastructure/Persistence/AppDbContext.cs:8-36`, `Notification.Infrastructure/Migrations/20261024103253_InitialNotificationSchema.cs`).
> **Cập nhật lần cuối:** 2026-10-24.

> **Cảnh báo:** mô tả entity trong tài liệu cũ (`Content`, `Type`, `ActionUrl`, `EmailEnabled`, `PushEnabled`, `OrderUpdates`, `AuctionAlerts`) **không tồn tại trong code**. Bảng dưới đây là trường thật đọc từ file nguồn.

---

## 1. MÔ HÌNH THỰC THỂ CHÍNH (DOMAIN ENTITIES)

### 1.1. `NotificationRecord` (DbSet tên `Notifications`)

Nguồn: `Notification.Domain/Entities/NotificationRecord.cs:6-24`; cấu hình: `Persistence/Configurations/NotificationRecordConfiguration.cs`.

| Trường | Kiểu | Ghi chú thật | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---|:---:|:---|
| `Id` | `Guid` | Khoá chính từ `BaseEntity<Guid>` | ✅ CÓ | `Domain/Base/BaseEntity.cs:3-10` |
| `UserId` | `Guid` | Người nhận | ✅ CÓ | `NotificationRecord.cs:8` |
| `SourceEventId` | `Guid?` | Idempotency theo event nguồn | ✅ CÓ | `:9` |
| `TemplateId` | `Guid?` | FK → `NotificationTemplate.Id` (khai báo thật) | ✅ CÓ | `:10`; `NotificationRecordConfiguration.cs:19-21` |
| `NotificationType` | enum `NotificationType?` | `Order`/`Auction`/`System`/`Promotion`; lưu dạng **string** (`HasConversion<string>()`) | ✅ CÓ | `:11`; `NotificationRecordConfiguration.cs:12` |
| `ChannelScope` | enum `ChannelScope` | `InApp`/`Email`/`Push`/`All`; lưu dạng string | ✅ CÓ | `:12`; `NotificationRecordConfiguration.cs:13` |
| `Title` | `string?` | Tối đa **500** ký tự (`HasMaxLength(500)`) | ✅ CÓ | `:13`; `NotificationRecordConfiguration.cs:14` |
| `Body` | `string?` | Nội dung, tối đa 4000 ký tự — **không có trường `Content`** | ✅ CÓ | `:14`; `NotificationRecordConfiguration.cs:15` |
| `Payload` | `string?` | Cột `jsonb`; deep link đặt trong đây — **không có `ActionUrl`** | ✅ CÓ | `:15`; `NotificationRecordConfiguration.cs:16` `HasColumnType("jsonb")` |
| `IsRead` | `bool` | Mặc định `false` | ✅ CÓ | `:16` |
| `ReadAt` | `DateTimeOffset?` | Thời điểm đọc | ✅ CÓ | `:17` |
| `Status` | enum `NotificationStatus` | `Pending`/`Sent`/`Failed`/`Read`; lưu dạng string | ✅ CÓ | `:18`; `NotificationRecordConfiguration.cs:17` |
| `CreatedAt`/`UpdatedAt`/`IsDeleted`/`DeletedAt` | — | Từ `BaseEntity`; **không có global query filter** cho `IsDeleted` | ⚠️ MỘT PHẦN | `BaseEntity.cs:3-10`; không `HasQueryFilter` trong `Persistence/Configurations/` |

**Chưa có method nghiệp vụ** như `MarkAsRead()` — entity là POCO thuần (`NotificationRecord.cs` không có method nào).

### 1.2. `NotificationPreference` (DbSet tên `NotificationPreferences`)

Nguồn: `Domain/Entities/NotificationPreference.cs:6-15` (navigation `Channel` ở `:14`); cấu hình: `Persistence/Configurations/NotificationPreferenceConfiguration.cs`.

| Trường | Kiểu | Ghi chú thật | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---|:---:|:---|
| `Id` | `Guid` | Khoá chính, kế thừa `BaseEntity<Guid>` | ✅ CÓ | `BaseEntity.cs:5` |
| `UserId` | `Guid` (required) | Người dùng | ✅ CÓ | `:8` |
| `NotificationType` | enum `NotificationType` (required) | Loại thông báo được cấu hình | ✅ CÓ | `:9` |
| `ChannelCode` | enum `ChannelType` | `Email`/`Push`/`InApp`/`Sms`; lưu dạng string max 50, FK → `NotificationChannels.Code` (unique) | ✅ CÓ | `:10`; `NotificationPreferenceConfiguration.cs:13-18` |
| `IsDisabled` | `bool` | **Cờ tắt** (nghịch đảo so với tài liệu cũ dùng cờ bật) | ✅ CÓ | `:11` |
| `EmailEnabled` / `PushEnabled` / `OrderUpdates` / `AuctionAlerts` | — | **KHÔNG TỒN TẠI** trong entity | ❌ CHƯA CÓ | Không có trong `NotificationPreference.cs` |

> Hệ quả: không có ràng buộc unique `UserId` như tài liệu cũ nói; mỗi user có thể có nhiều bản ghi (UserId × NotificationType × ChannelCode).

### 1.3. Bảng trạng thái 10 entity (đã từng được ghi dữ liệu chưa?)

| Entity | DbSet | Có code ghi (ngoài seed/migration)? | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---|:---:|:---|
| `NotificationChannel` | `NotificationChannels` | Seed 4 kênh | ⚠️ CHỈ SEED | `Seeds/NotificationSeedData.cs:15-58` |
| `NotificationTemplate` | `NotificationTemplates` | Seed 2 mẫu | ⚠️ CHỈ SEED | `:64-95` |
| `NotificationPreference` | `NotificationPreferences` | **Không** | ❌ CHƯA CÓ | 0 lần ghi; 0 lần đọc ngoài DbSet |
| `DeviceToken` | `DeviceTokens` | **Không** | ❌ CHƯA CÓ | 0 lần ghi/đọc |
| `NotificationRecord` | `Notifications` | **Không** (chỉ seed 1 bản ghi) | ❌ CHƯA CÓ | Seed `:98-116`; `SendOtpEmailConsumer.cs` không inject DbContext |
| `NotificationDelivery` | `NotificationDeliveries` | **Không** | ❌ CHƯA CÓ | 0 lần ghi/đọc |
| `NotificationLog` | `NotificationLogs` | **Không** | ❌ CHƯA CÓ | 0 lần ghi/đọc |
| `EventLog` | `EventLogs` | **Không** | ❌ CHƯA CÓ | 0 lần ghi/đọc |
| `BrokerHealthCheck` | `BrokerHealthChecks` | **Không** | ❌ CHƯA CÓ | 0 lần ghi/đọc |
| `AuditLog` | `AuditLogs` | **Không** | ❌ CHƯA CÓ | 0 lần ghi/đọc |

> `INotificationDbContext` được DI đăng ký tại `Infrastructure/DependencyInjection.cs:22` nhưng **không có consumer/service nào inject** ⇒ toàn bộ DbSet trên thực tế chưa được dùng ở runtime.

---

## 2. QUY CHUẨN CƠ SỞ DỮ LIỆU & EF CORE

| Hạng mục | Giá trị thật | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| Engine | PostgreSQL, provider `Npgsql.EntityFrameworkCore.PostgreSQL` | ✅ CÓ | `Notification.Infrastructure.csproj`; `DependencyInjection.cs:20` `UseNpgsql` |
| Database / port | `notification_db` @ `5439` | ✅ CÓ | `appsettings.json:10`; `docker-compose.yml:19` |
| Chuỗi kết nối | Đọc từ `ConnectionStrings:NotificationDb`, fallback hard-code mật khẩu mặc định | ⚠️ RỦI RO | `DependencyInjection.cs:16-18` (secret thật trong repo) |
| Tên bảng | **PascalCase** theo tên DbSet: `Notifications`, `NotificationPreferences`, `NotificationChannels`, `NotificationTemplates`, `DeviceTokens`, `NotificationDeliveries`, `NotificationLogs`, `EventLogs`, `BrokerHealthChecks`, `AuditLogs` | ✅ CÓ | `AppDbContext.cs:14-23`; migration `:130,194,169,267,64,298,335,84,43,18` |
| Index bắt buộc `(UserId, IsRead, CreatedAt)` | **KHÔNG TỒN TẠI** | ❌ CHƯA CÓ | Không có trong `NotificationRecordConfiguration.cs`; index thật xem §3 |
| Global query filter cho soft delete | **KHÔNG CÓ** dù `IsDeleted` tồn tại | ❌ CHƯA CÓ | `BaseEntity.cs:3-10`; không `HasQueryFilter` ở 10 file `Persistence/Configurations/` |
| No Repository Pattern | ✅ Tuân thủ — Application dùng trực tiếp `INotificationDbContext` | ✅ CÓ | `Application/Interfaces/INotificationDbContext.cs:8-21`; 0 file `*Repository.cs` |
| Migrate tự động khi khởi động | `Database.Migrate()` trong `Program.cs:33-47` | ✅ CÓ | `Notification.Api/Program.cs:33-47` |
| `NotificationDbContext` (lớp bọc) | Tồn tại nhưng **không đăng ký DI, không dùng** | ❌ CODE CHẾT | `Persistence/AppDbContext.cs:38-43` |

---

## 3. DANH SÁCH 13 BẢNG TRONG MIGRATION ĐẦU TIÊN

Migration: `Notification.Infrastructure/Migrations/20261024103253_InitialNotificationSchema.cs`.

| Bảng | Loại | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `AuditLogs` | Entity nghiệp vụ | ✅ TẠO | `:18` |
| `BrokerHealthChecks` | Entity nghiệp vụ | ✅ TẠO | `:43` |
| `DeviceTokens` | Entity nghiệp vụ | ✅ TẠO | `:64` |
| `EventLogs` | Entity nghiệp vụ | ✅ TẠO | `:84` |
| `InboxState` | Bảng hạ tầng MassTransit | ✅ TẠO | `:107` |
| `NotificationChannels` | Entity nghiệp vụ | ✅ TẠO | `:130` |
| `OutboxState` | Bảng hạ tầng MassTransit | ✅ TẠO | `:153` |
| `NotificationPreferences` | Entity nghiệp vụ | ✅ TẠO | `:169` |
| `NotificationTemplates` | Entity nghiệp vụ | ✅ TẠO | `:194` |
| `OutboxMessage` | Bảng hạ tầng MassTransit | ✅ TẠO | `:225` |
| `Notifications` | Entity nghiệp vụ | ✅ TẠO | `:267` |
| `NotificationDeliveries` | Entity nghiệp vụ | ✅ TẠO | `:298` |
| `NotificationLogs` | Entity nghiệp vụ | ✅ TẠO | `:335` |

**Index unique đang có (đọc từ `Persistence/Configurations/`):**

| Index | Bảng | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| Unique `Token` | `DeviceTokens` | ✅ CÓ | `DeviceTokenConfiguration.cs:14` |
| Unique `Code` | `NotificationChannels` | ✅ CÓ | `NotificationChannelConfiguration.cs:13` |
| Unique `(Code, Locale)` | `NotificationTemplates` | ✅ CÓ | `NotificationTemplateConfiguration.cs:18` |
| `(UserId, IsRead, CreatedAt)` | `Notifications` | ❌ CHƯA CÓ | Không khai báo — truy vấn "thông báo chưa đọc của user" sẽ full scan |

**Concurrency:** `NotificationDelivery.RowVersion` được cấu hình `IsConcurrencyToken()` — chỉ entity này có kiểm soát đồng thời (`NotificationDeliveryConfiguration.cs:17`).

**Quan hệ thật:** `NotificationRecord` 1-n `NotificationDelivery` và `NotificationLog` (`NotificationRecordConfiguration.cs:23-29`); `NotificationDelivery` FK tới `NotificationChannel.Code` (`NotificationDeliveryConfiguration.cs:23-26`).
