# 01 — PHÂN TẦNG CLEAN ARCHITECTURE & QUY TẮC PHỤ THUỘC (NOTIFICATION SERVICE)

Solution `Notification.sln` bao gồm 4 project tương ứng với 4 tầng kiến trúc sạch (Clean Architecture):

```
Notification.sln
├── Notification.Domain/           # Entities (Notification, NotificationPreference), Enums (NotificationType)
├── Notification.Application/      # Use cases CQRS, Event Consumers (OrderConsumer, BidConsumer...)
├── Notification.Infrastructure/   # EF Core DbContext, PostgreSQL mappings, Email/Push Senders
└── Notification.Api/              # Điểm khởi chạy gRPC Server (Program.cs, DI setup)
```

---

## 1. NGUYÊN TẮC LUỒNG PHỤ THUỘC (DEPENDENCY RULE)

```
[ Notification.Api ] ────────────┐
                                 │
                                 ▼
               [ Notification.Application ] ──▶ [ Notification.Domain ]
                                 ▲
                                 │
[ Notification.Infrastructure ] ─┘
```
- **`Notification.Domain`**: Chứa Entity `Notification`, `NotificationPreference`. Phương thức nghiệp vụ `notification.MarkAsRead()` phải nằm trong Domain Entity.
- **`Notification.Application`**: Chứa các Handlers xử lý gRPC và các MassTransit Consumers nhận sự kiện từ các service khác.
- **`Notification.Infrastructure`**: Chứa `NotificationDbContext`, các adapter gửi email (SMTP/SendGrid) hoặc Push (Firebase FCM).
- **`Notification.Api`**: Điểm khởi động gRPC Server, đăng ký `app.MapGrpcService<NotificationGrpcService>()`.
