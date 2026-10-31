# 03 — SECURITY HEADERS, INTERNAL API & IDENTITY (NOTIFICATION SERVICE)

## 1. Controller Segregation
* `Notification.Api/Controllers/Public/`: Serves public notification actions (`NotificationsController`).
* `Notification.Api/Controllers/Internal/`: Serves internal email dispatch (`InternalEmailController` at `/api/v1/internal/notifications/email/otp` and `/api/v1/internal/notifications/email/send`).

## 2. Internal Email Dispatch Protection
* Direct email sending endpoints are strictly internal to prevent external spam or abuse.
* Protected by `InternalServiceAuthMiddleware` checking header `X-Internal-Service-Key`. Missing or invalid key returns HTTP `403 Forbidden`.
* Never expose `/api/v1/internal/notifications/email/*` to API Gateway.

## 3. Trusted Identity Extraction & Notification Security
* Never trust client query parameters for `userId`.
* Operations on own notifications (`/api/v1/notifications/me`, `preferences`, `devices`) must extract caller identity from `this.TryGetUserId(out var userId, out var authError)`.
* Prohibit defaulting to fallback GUIDs when unauthenticated.
