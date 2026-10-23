# NOTIFICATION SERVICE SPEC KIT — CHỈ DẪN KỸ THUẬT CHO AI AGENT & DEVELOPER

Tài liệu này là **Bộ Chỉ Dẫn & Đặc Tả Kỹ Thuật (Spec Kit)** cho repository **NotificationService** thuộc hệ sinh thái Microservices **Nexus**. Giúp lập trình viên và các AI Coding Agent (Cursor, Claude, Copilot, DeepSeek...) nắm bắt bối cảnh quản lý thông báo, cấu hình tùy chọn thông báo người dùng và **loại bỏ hoàn toàn nguy cơ ảo giác (zero hallucination)**.

---

## 1. MỤC LỤC BỘ ĐẶC TẢ NOTIFICATION SERVICE (SPECS/)

| File Đặc Tả | Trọng Tâm Nội Dung |
|:---|:---|
| [00_AGENT_MISSION_GUARDRAILS.md](00_AGENT_MISSION_GUARDRAILS.md) | Bounded Context, Port `8007`, DB `notification_db` (`5439`), các điều cấm kỵ cốt lõi. |
| [01_CLEAN_ARCHITECTURE_LAYERS.md](01_CLEAN_ARCHITECTURE_LAYERS.md) | Cấu trúc 4 tầng Clean Architecture (`.Api`, `.Application`, `.Domain`, `.Infrastructure`), luồng phụ thuộc. |
| [02_API_GRPC_CONTRACTS.md](02_API_GRPC_CONTRACTS.md) | Hợp đồng gRPC `NotificationApi` trong `shop.proto`, danh sách thông báo, đánh dấu đọc, preferences. |
| [03_EVENT_MESSAGING.md](03_EVENT_MESSAGING.md) | Người nghe sự kiện trung tâm (Global Event Consumer) từ toàn bộ hệ thống qua RabbitMQ. |
| [04_DATA_PERSISTENCE.md](04_DATA_PERSISTENCE.md) | Mô hình thực thể `Notification`, `NotificationPreference`, DbContext EF Core 8 (No Repository Pattern). |
| [05_DEV_VERIFICATION_CHECKLIST.md](05_DEV_VERIFICATION_CHECKLIST.md) | Lệnh biên dịch, kiểm thử tự động, checklist kiểm tra trước khi commit. |

---

## 2. THÔNG SỐ VẬN HÀNH CHÍNH

- **Ngôn ngữ & Runtime**: .NET 8 (C# 12).
- **Cổng gRPC Server**: Port `8007` (Không có cổng HTTP REST riêng, mọi REST call đều qua ApiGateway Port `8000`).
- **Cơ Sở Dữ Liệu**: PostgreSQL (Port `5439`, Database: `notification_db`, Mật khẩu: `MatkhaucuaTui123@`).
- **Message Broker**: RabbitMQ (Port `5672`, Virtual Host: `/`).
