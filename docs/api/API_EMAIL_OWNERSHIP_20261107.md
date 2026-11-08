# API email and device ownership — pre-code contract

Status: VERIFIED by local build and automated tests. Date: 2026-11-07.

## 1. Routing and authorization
Email is exposed only by EmailController under /api/v1/notifications/email: POST otp, send, seller-rejection. Remove InternalEmailController and duplicate internal email routes. Internal service-key middleware stays disabled per local policy. Existing internal in-app endpoint is outside this email-focused change.
DELETE /api/v1/notifications/devices/{token} requires authenticated caller identity. Only an active, non-deleted token owned by that user can be revoked. A missing/other-user token is an idempotent 204 with no mutation, preventing existence disclosure.

## 2. Input and validation
Request records live in Application/Services/Email/EmailRequests.cs. OTP requires Email and OtpCode; FullName defaults to Customer when blank; ExpiresInMinutes is 1-60, default 5; Purpose defaults to registration when blank. Supported purposes: registration, password_reset, seller_registration. General email requires ToEmail, Subject, HtmlBody. Seller rejection requires Email and RejectReason; FullName/ShopName have defaults. DataAnnotations produce 400 VAL_INVALID_INPUT through ApiValidationProblemFactory.

## 3. Output
Controllers return Application DTOs directly, no outer success/data envelope. OTP response reports Status Queued when accepted by the background queue, or Sent for direct sender fallback. General/rejection response reports Sent only when sender returns true. Simulation semantics are an existing provider limitation outside this change. Email failures throw EMAIL_SEND_FAILED (500) and use centralized ProblemDetails. Device revoke returns 204. Email request JSON field names remain camelCase.

## 4. Errors and enums
400 VAL_INVALID_INPUT: malformed email, missing required fields, expiry outside 1-60, unsupported OTP purpose.
401 UNAUTHORIZED: device operation missing caller identity.
500 EMAIL_SEND_FAILED: synchronous sender returns false.
Unknown/other-user/repeated token revoke: 204, no mutation or new audit.
No existing persisted enum value is changed. Add DeviceRevoke after existing AuditAction values; no schema migration required because enums persist as integers.

## 5. Implementation and verification
- Preserve OTP purpose in public request, Application DTO, queue and worker sender call.
- Move HTTP validation/request/response contracts to Application; keep controller thin.
- Device revoke passes caller ID to service; appends audit via AddAuditLog in the same SaveChanges as the mutation. Never log raw device token in audit.
- Test validation, real MVC controller discovery/Swagger uniqueness, OTP worker purpose, foreign-user token protection, unauthorized request and idempotency.
- Build/test Release. Verify route map from Swagger without starting production DB/SMTP.

## 5. Verification
- [x] Contract documented before C# changes in docs/api/API_EMAIL_OWNERSHIP_20261107.md.
- [x] Release build: 0 errors, 0 warnings.
- [x] Release tests: 47 passed, 0 failed, 0 skipped.
- [x] Swagger fetched over HTTP from a test host discovering the real API controllers.
- Production database/SMTP were not contacted. No schema migration was applied.

## Final risk and release boundary
GitNexus aggregate change assessment: CRITICAL (22 tracked files, 97 symbols, 18 affected processes). Build and 47 tests passed; this does not establish external caller compatibility. Before shared-environment rollout, callers must migrate removed internal email URLs and success/data envelope parsing to the public DTO contract. No commit or deployment was performed.
