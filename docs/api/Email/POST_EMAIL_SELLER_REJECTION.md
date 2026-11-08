# POST /api/v1/notifications/email/seller-rejection

Status: VERIFIED (local automated tests), 2026-11-07.

## 1. Routing and authorization
EmailController, public route only. Replaces the old internal rejection action; no service-key check per current policy.

## 2. Request
SendSellerRejectionEmailRequest in Application/Services/Email/EmailRequests.cs. Email and RejectReason required. FullName defaults to Customer; ShopName defaults to Your Shop. HTML-encode user-supplied text in the Application email service.

## 3. Response
HTTP 200 EmailDispatchResponse: recipient, status (Sent), dispatchedAt. No outer envelope. Existing SMTP simulation limitation applies.

## 4. Errors and enums
400 VAL_INVALID_INPUT for invalid email or empty reason. 500 EMAIL_SEND_FAILED for synchronous send failure. Centralized ProblemDetails. No persisted enum changes.

## 5. Verification
- [x] Contract documented before C# changes in docs/api/API_EMAIL_OWNERSHIP_20261107.md.
- [x] Release build: 0 errors, 0 warnings.
- [x] Release tests: 47 passed, 0 failed, 0 skipped.
- [x] Swagger fetched over HTTP from a test host discovering the real API controllers.
- Production database/SMTP were not contacted. No schema migration was applied.
