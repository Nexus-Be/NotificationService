# 02 — HỢP ĐỒNG GIAO TIẾP GRPC (NOTIFICATION SERVICE GRPC CONTRACTS)

> **Trạng thái triển khai (2026-10-24):** ❌ CHƯA CÓ — **0/4 rpc** được implement; không có `*.proto`, không có package `Grpc.AspNetCore`, không `MapGrpcService`; service chỉ có `GET /health`.
> **Nguồn sự thật:** code (`Notification.Api/Program.cs:49`, `Notification.Api/Notification.Api.csproj:15-18`).
> **Cập nhật lần cuối:** 2026-10-24.

> **Cảnh báo:** toàn bộ mục §1 là **đặc tả mục tiêu**, chưa có trong code. Không được coi `NotificationApi` là hợp đồng đang chạy, và **không** trích dẫn nó như bằng chứng tích hợp Gateway.

---

## 1. DANH SÁCH CÁC RPC METHODS (ĐẶC TẢ — CHƯA TRIỂN KHAI)

Hợp đồng đích: service `NotificationApi` trong `Contracts/shop.proto`. **File này không tồn tại trong repo** (không có `*.proto` ở bất kỳ thư mục nào) và `Notification.Api/` cũng không có thư mục `Contracts/`.

```protobuf
service NotificationApi {
  // Lấy danh sách thông báo của người dùng
  rpc ListNotifications(UserRequest) returns (NotificationPage);

  // Đánh dấu thông báo đã đọc
  rpc MarkNotificationRead(IdRequest) returns (Notification);

  // Lấy cài đặt tùy chọn nhận thông báo
  rpc GetPreferences(UserRequest) returns (NotificationPreferences);

  // Cập nhật cài đặt nhận thông báo
  rpc UpdatePreferences(UpdateNotificationPreferencesRequest) returns (NotificationPreferences);
}
```

| # | RPC | Trạng thái | Bằng chứng (file:line) |
|:--|:---|:---:|:---|
| 1 | `ListNotifications(UserRequest) → NotificationPage` | ❌ CHƯA CÓ | Không có `.proto`; không có class kế thừa `NotificationApiBase`; `Program.cs:49` chỉ `MapGet("/health")` |
| 2 | `MarkNotificationRead(IdRequest) → Notification` | ❌ CHƯA CÓ | Như trên; cũng không có handler/use case nào trong `Application/Services/` |
| 3 | `GetPreferences(UserRequest) → NotificationPreferences` | ❌ CHƯA CÓ | Như trên; entity `NotificationPreference` tồn tại nhưng không có truy vấn |
| 4 | `UpdatePreferences(UpdateNotificationPreferencesRequest) → NotificationPreferences` | ❌ CHƯA CÓ | Như trên |

**Bằng chứng vắng mặt (kiểm chứng được):**

| Hạng mục cần có cho gRPC | Trạng thái | Bằng chứng (file:line) |
|:---|:---:|:---|
| File `.proto` | ❌ | `find . -name "*.proto"` → 0 kết quả |
| Package `Grpc.AspNetCore` | ❌ | `Notification.Api/Notification.Api.csproj:15-18` chỉ có `Microsoft.AspNetCore.OpenApi`, `Swashbuckle.AspNetCore`, `MassTransit.RabbitMQ`, `Microsoft.EntityFrameworkCore.Design` |
| `builder.Services.AddGrpc()` | ❌ | `Program.cs:9-19` không có |
| `app.MapGrpcService<T>()` | ❌ | `Program.cs` không có; dòng `Map*` duy nhất là `:49` |
| HTTP/2 endpoint | ❌ | `Dockerfile:4` đặt `ASPNETCORE_URLS=http://+:8007` (HTTP/1.1); không cấu hình Kestrel HTTP/2 |
| Authentication cho rpc | ❌ | `Program.cs` không có `AddAuthentication`/`UseAuthentication`; không đọc header `x-user-id` |

---

## 2. QUY CHUẨN XỬ LÝ LỖI & MÃ LỖI (ĐẶC TẢ CHO TƯƠNG LAI)

Khi (và chỉ khi) có `NotificationGrpcService`, các quy ước sau là **đặc tả**, chưa có code:

- **`StatusCode.InvalidArgument`** (400): `UserId` hoặc `Id` rỗng/sai định dạng UUID.
- **`StatusCode.NotFound`** (404): không tìm thấy thông báo với ID yêu cầu.
- **`StatusCode.PermissionDenied`** (403): user đánh dấu đọc thông báo của người khác.

**Hiện trạng xử lý lỗi của service** (HTTP, không phải gRPC):

| Hành vi | Trạng thái | Bằng chứng (file:line) |
|:---|:---:|:---|
| `EntityNotFoundException` → 404 + `NOT_FOUND` | ✅ CÓ | `Api/Middleware/GlobalExceptionHandlerMiddleware.cs:38-41` |
| `BusinessRuleValidationException` → 400 + `BUSINESS_RULE_VIOLATION` | ✅ CÓ | `:43-47` |
| `DomainException`/`ArgumentException` → 400 | ✅ CÓ | `:48-57` |
| Lỗi khác → 500 + `INTERNAL_SERVER_ERROR` | ✅ CÓ | `:58-62` |
| Link RFC 9110 đúng theo status | ⚠️ SAI | `:79` luôn dùng `15.5.1` cho mọi 4xx, nên 404 trỏ sai section (đúng phải `15.5.5`) |
| `traceId` trong response | ✅ CÓ | `:34`, `:84` (lấy `Activity.Current?.Id` hoặc `TraceIdentifier`; **không** đọc `x-trace-id` từ Gateway) |

> Lưu ý: middleware hiện **chưa từng chạy** với lỗi 404 thật vì service không có route nào ngoài `/health`; 404 do routing trả về trước middleware. Đây là lỗi tiềm ẩn sẽ lộ ra ngay khi thêm controller/gRPC.
