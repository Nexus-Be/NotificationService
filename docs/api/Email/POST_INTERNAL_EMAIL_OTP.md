# Removed internal email routes

Updated: 2026-11-07.

InternalEmailController was removed to eliminate route collisions and follow the public-route policy. POST /api/v1/internal/notifications/email/otp, /send and /seller-rejection now return 404. Use /api/v1/notifications/email/otp, /send and /seller-rejection respectively. OTP accepts purpose and reports Queued after enqueue. Responses are pure DTOs.

See POST_EMAIL_OTP.md, POST_EMAIL_SEND.md and POST_EMAIL_SELLER_REJECTION.md in this directory. This change does not move the separate internal in-app endpoint.
