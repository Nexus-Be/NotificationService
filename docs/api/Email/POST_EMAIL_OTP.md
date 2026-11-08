# POST /api/v1/notifications/email/otp

Status: VERIFIED — local automated tests,2026-11-08. This replaces the earlier RAM-only contract. Full contract and handoff: [OTP delivery](../Otp/OTP_DELIVERY.md).

Requires trusted Gateway service/admin role; anonymous401, buyer/seller/staff403. Frontend calls User Service. Body16KiB, no-store. Existing email/fullName/otpCode/expiresInMinutes/purpose retained; optional requestId/userId/issuedAt/expiresAt/traceId added. Issued/expiry supplied together; supported purposes registration/password_reset/seller_registration. Same ID/body replays; changed409; new challenge must have later issuedAt in email+purpose group. User Service must invalidate the previous OTP when resending.

HTTP200 pure OtpEmailDispatchResponse: recipient/fullName/expiresInMinutes/purpose/status/acceptedAt/requestId/expiresAt/isReplay. Runtime Queued means committed encrypted DB queue; ProviderAccepted means SMTP accepted, never account verification. Other states Sending/Failed/Expired/Superseded. GET /api/v1/notifications/otp-dispatches/{id} same roles returns masked metadata/status. No OTP/ciphertext/fingerprint echo.

DurableOtpWorker uses strict SMTP sender, bounded retry/timeout/expiry and lease/fencing, atomic secret-free audit. Missing SMTP config fails asynchronously, never simulated success. Terminal payload is erased; proof remains protected for replay. Old standalone constructors retain legacy RAM/direct behavior for compatibility only, production DI uses the durable service.

Errors:400 VAL_INVALID_INPUT/OTP_EXPIRED,401 UNAUTHORIZED,403 FORBIDDEN,404 OTP_DISPATCH_NOT_FOUND (GET),409 OTP_REQUEST_CONFLICT/OTP_REQUEST_OBSOLETE,503 OTP_PROTECTION_UNAVAILABLE for unreadable replay proof; request size uses global413 handling. Audit enum additions8..10 do not change existing ints.

Migration20261108095031_AddProtectedOtpDispatches and persistent/shared Data Protection key ring required before rollout. Build Release0 warnings/errors;257 tests pass,0fail/skip. Project DB not migrated; User Service and real SMTP integration pending. Password-change confirmation is a separate next step.
