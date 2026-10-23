# 05 — DANH SÁCH KIỂM CHỨNG & NGHIỆM THU (NOTIFICATION SERVICE CHECKLIST)

---

## 1. LỆNH BIÊN DỊCH & CHẠY THỬ NGHIỆM

```bash
# 1. Biên dịch solution Notification
dotnet build Notification.sln -c Release

# 2. Khởi chạy service (gRPC port 8007)
dotnet run --project Notification.Api

# 3. Thử nghiệm qua ApiGateway:
# Mở gateway.runtime.json, đổi "notification": { "Mode": "Real", "Address": "http://localhost:8007" }
# Gọi GET /gateway/api/v1/notifications trên Swagger Gateway xem kết quả thật trả về.
```

---

## 2. CHECKLIST TRƯỚC KHI COMMIT CHO AI AGENTS

- [ ] **Biên dịch thành công 100%**: Không lỗi cảnh báo nghiêm trọng.
- [ ] **Không vi phạm rào chắn**: Không dùng Repository Pattern bọc ngoài EF Core.
- [ ] **Port & Database**: Lắng nghe đúng port `8007`, kết nối `notification_db` trên port `5439` (Password: `MatkhaucuaTui123@`).
- [ ] **Git Status**: Không commit file rác (`bin/`, `obj/`, `.idea/`, `.env`).
