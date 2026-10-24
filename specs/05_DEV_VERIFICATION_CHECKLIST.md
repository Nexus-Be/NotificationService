# 05 — DANH SÁCH KIỂM CHỨNG & NGHIỆM THU (NOTIFICATION SERVICE CHECKLIST)

> **Trạng thái triển khai (2026-10-24):** ⚠️ MỘT PHẦN — có 2 test case chạy được và build 0 error, nhưng bước nghiệm thu qua ApiGateway **bất khả thi** vì service không có rpc nào.
> **Nguồn sự thật:** code (`Notification.Tests/EmailSenderTests.cs:18,40`, `Notification.Api/Program.cs:49`).
> **Cập nhật lần cuối:** 2026-10-24.

> Lần cập nhật này **chỉ sửa tài liệu** — không chạy `dotnet build`/`dotnet test`. Các số liệu build/test dưới đây lấy từ lần đo trên commit `4d8e46d`.

---

## 1. LỆNH BIÊN DỊCH & CHẠY THỬ NGHIỆM

```bash
# 1. Biên dịch solution Notification (5 project)
dotnet build Notification.sln -c Release

# 2. Chạy kiểm thử tự động (hiện có 2 case)
dotnet test Notification.sln -c Release

# 3. Khởi chạy service — HTTP/1.1 tại cổng 8007, KHÔNG phải gRPC
dotnet run --project Notification.Api
curl -i http://localhost:8007/health
```

**Kết quả mong đợi (đã đo):**

| Bước | Kỳ vọng | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `dotnet build` | 0 error, 5 project | ✅ CÓ | Audit commit `4d8e46d`; `Notification.sln:4-13` |
| `dotnet test` | 2 passed | ⚠️ MỘT PHẦN | `Notification.Tests/EmailSenderTests.cs:18`, `:40` — cả 2 đều **không chạm DB** |
| `GET /health` | 200 `{"status":"Healthy","service":"Notification.Api"}` | ✅ CÓ | `Program.cs:49` |
| Bất kỳ route nghiệp vụ nào (`/api/v1/notifications`, gRPC `NotificationApi`) | **404 / không tồn tại** | ❌ CHƯA CÓ | `Program.cs` chỉ có 1 `Map*` |
| Health check phản ánh DB | **Không** — `/health` trả hằng số, không query DB | ❌ CHƯA CÓ | `Program.cs:49` |

> **Cảnh báo:** `/health` **không** kiểm tra kết nối PostgreSQL hay RabbitMQ. Không được dùng nó làm bằng chứng service "hoạt động".

### 1.1. Nghiệm thu qua ApiGateway — HIỆN KHÔNG THỰC HIỆN ĐƯỢC

Bước "đổi `notification.Mode = Real`, gọi `GET /gateway/api/v1/notifications`" trong tài liệu cũ **không thể chạy**:

| Điều kiện cần | Trạng thái | Bằng chứng (file:line) |
|:---|:---:|:---|
| Service có rpc `ListNotifications` | ❌ CHƯA CÓ | 0/4 rpc; `specs/02_API_GRPC_CONTRACTS.md` §1 |
| Service mở HTTP/2 cho gRPC | ❌ CHƯA CÓ | `Dockerfile:4` `ASPNETCORE_URLS=http://+:8007` |
| Gateway có client `notifications` | ❌ CHƯA CÓ | Gateway chỉ có 1 client `users` |
| Route `/gateway/api/v1/notifications` tồn tại | ❌ CHƯA CÓ | Không có controller/route này ở service |

---

## 2. CHECKLIST TRƯỚC KHI COMMIT (CHO AI AGENTS)

- [ ] **Biên dịch sạch**: `dotnet build Notification.sln -c Release` không lỗi.
- [ ] **Không vi phạm rào chắn**: không thêm `*Repository.cs`, không `UnitOfWork` bọc EF Core.
- [ ] **Cổng bất biến**: service `8007`, DB `5439` (`notification_db`). Không hard-code thêm secret — mật khẩu mặc định hiện nằm trong `appsettings.json:10`, `.env.example:8`, `docker-compose.yml:17`, `DependencyInjection.cs:17` và **phải** chuyển sang biến môi trường.
- [ ] **Git sạch**: không commit `bin/`, `obj/`, `.idea/`, `.env`.
- [ ] **Bảng trạng thái tài liệu**: nếu thêm tính năng, cập nhật `Trạng thái` + `Bằng chứng (file:line)` trong `README.md` §2.1 và `specs/04` §1.3.

**Trạng thái checklist tại thời điểm 2026-10-24:**

| Mục | Trạng thái | Bằng chứng (file:line) |
|:---|:---:|:---|
| Build sạch | ✅ CÓ | Audit `4d8e46d` (không phải lần đo của tôi) |
| Test tồn tại và pass | ⚠️ 2/2 case, phạm vi hẹp | `Notification.Tests/EmailSenderTests.cs:18,40` |
| Không Repository Pattern | ✅ CÓ | 0 file `*Repository.cs` |
| Cổng 8007 / DB 5439 | ✅ CÓ | `Dockerfile:4`, `appsettings.json:10` |
| Secret tách khỏi repo | ❌ CHƯA CÓ | 4 file chứa `MatkhaucuaTui123@` (xem trên) |
| CI chạy build + test | ❌ CHƯA CÓ | `.github/workflows/ci-cd.yml:38-47` chỉ Docker buildx |
| Test chạm DB / consumer ghi DB | ❌ CHƯA CÓ | `SendOtpEmailConsumer.cs:11-20` không inject DbContext |
