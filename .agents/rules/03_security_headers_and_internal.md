# 03 — SECURITY HEADERS, INTERNAL API & IDENTITY (NOTIFICATION SERVICE)

## 1. Public Controllers And Temporary Internal Disable
* All controllers live under `Notification.Api/Controllers/Public/`. There is no `Controllers/Internal/` folder.
* Internal API security is temporarily disabled. `InternalServiceAuthMiddleware` registration is commented; no route requires `X-Internal-Service-Key`.
* Inter-service calls use plain REST routes: `POST /api/v1/notifications/email/otp`, `POST /api/v1/wallets/checkout/direct`, `POST /api/v1/inventories/initialize`, `POST /api/v1/logs/batch`.
* Re-enabling the old model requires explicit approval and restoring middleware, handler, Gateway blocking, and docs together.

## 3. Trusted Identity Extraction & Notification Security
* Never trust client query parameters for `userId`.
* Operations on own notifications (`/api/v1/notifications/me`, `preferences`, `devices`) must extract caller identity from `this.TryGetUserId(out var userId, out var authError)`.
* Prohibit defaulting to fallback GUIDs when unauthenticated.
