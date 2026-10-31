# 01 — CLEAN ARCHITECTURE & BOUNDED CONTEXT (NOTIFICATION SERVICE)

## 1. Bounded Context & Port Allocation
* Bounded Context: In-app user notifications, notification preferences, device tokens, system email dispatch (SMTP/OTP).
* Service Port: `8007` (HTTP REST API).
* Database: PostgreSQL `notification_db` on port `5439`.

## 2. 4-Layer Clean Architecture
* `Notification.Domain`: Entities (`NotificationRecord`, `NotificationPreference`, `DeviceToken`), Enums (`NotificationType`, `NotificationStatus`).
* `Notification.Application`: Use cases in `Services/{Feature}/`, interface `INotificationDbContext`, request/response records.
* `Notification.Infrastructure`: `AppDbContext`, EF Core configurations, migrations, SMTP email sender.
* `Notification.Api`: Thin controllers in `Controllers/Public/` and `Controllers/Internal/`, middlewares, extensions, composition root (`Program.cs`).

## 3. Feature Co-Location (Mandatory)
* `Services/Notifications/`: `IUserNotificationService.cs`, `UserNotificationService.cs`, `NotificationRequests.cs`, `NotificationResponses.cs`.
* `Services/Email/`: `IEmailNotificationService.cs`, `EmailNotificationService.cs`, `EmailRequests.cs`, `EmailResponses.cs`.

## 4. Thin 1-to-1 Controller Pairing
* `NotificationsController.cs` -> injects `IUserNotificationService`.
* `InternalEmailController.cs` -> injects `IEmailNotificationService`.

## 5. Absolute Architectural Prohibitions
* 🛑 Prohibit Repository Pattern over EF Core (inject `INotificationDbContext` directly).
* 🛑 Prohibit MediatR, gRPC, and Message Brokers.
