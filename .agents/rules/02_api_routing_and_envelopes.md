# 02 — API ROUTING, ENVELOPES & PAGINATION (NOTIFICATION SERVICE)

## 1. Route Conventions in NotificationService
* Collections: Lowercase plural nouns (`/api/v1/notifications`, `/api/v1/notifications/me`, `/api/v1/notifications/devices`, `/api/v1/notifications/preferences`, `/api/v1/admin/notifications`).
* Prohibit singular nouns (`/api/v1/notification`).
* One route attribute per action. Use single-line XML `<summary>`.

## 2. Pure DTO Return & Zero ResultFilter
* Controllers return pure DTOs:
  * `Ok(preferences)`
  * `Ok(notificationsList)`
  * `NoContent()` (for device token revocation)
  * `Ok(records)` (for health checks)
* Zero ResultFilter: Do NOT apply `ResultFilter` or manual envelope factories in `Notification.Api`. Gateway automatically wraps raw DTOs in `ApiResponse<T>`.

## 3. Database Pagination Governance
* Database-level pagination: `.Skip((page - 1) * pageSize).Take(pageSize)` and `.CountAsync()`.
* Output: Standard pagination structure containing `items` and `pagination` metadata.
