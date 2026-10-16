# 02 — HỢP ĐỒNG GIAO TIẾP GRPC (NOTIFICATION SERVICE GRPC CONTRACTS)

Dịch vụ gRPC của NotificationService được định nghĩa trong hợp đồng duy nhất `Contracts/shop.proto` dưới service `NotificationApi`:

---

## 1. DANH SÁCH CÁC RPC METHODS

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

---

## 2. QUY CHUẨN XỬ LÝ LỖI & MÃ LỖI (ERROR STATUS CODES)

Khi xử lý trong `NotificationGrpcService`, ném ra `RpcException`:
- **`StatusCode.InvalidArgument`** (400 Bad Request): `UserId` hoặc `Id` rỗng/sai định dạng UUID.
- **`StatusCode.NotFound`** (404 Not Found): Không tìm thấy thông báo với ID yêu cầu.
- **`StatusCode.PermissionDenied`** (403 Forbidden): User cố gắng đánh dấu đọc thông báo của người khác.
