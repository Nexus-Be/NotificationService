# POST /api/v1/notifications/email/send

Status: VERIFIED (local automated tests), 2026-11-07.

## 1. Routing and authorization
EmailController, public route only. Old internal email route removed. No service-key check per current policy.

## 2. Request
SendGeneralEmailRequest in Application/Services/Email/EmailRequests.cs. Required ToEmail (valid email), Subject and HtmlBody. Validation is declarative; controller contains no manual validation.

## 3. Response
HTTP 200 EmailDispatchResponse: recipient, status (Sent), dispatchedAt (UTC). Pure DTO, no success/data envelope. SMTP sender simulation still returns success when credentials are missing; Sent does not prove arrival at the recipient mailbox.

## 4. Errors and enums
400 VAL_INVALID_INPUT for malformed input. 500 EMAIL_SEND_FAILED when the sender returns false, formatted by centralized exception middleware. No persisted enum changes.

## 5. Verification
- [x] Contract documented before C# changes in docs/api/API_EMAIL_OWNERSHIP_20261107.md.
- [x] Release build: 0 errors, 0 warnings.
- [x] Release tests: 47 passed, 0 failed, 0 skipped.
- [x] Swagger fetched over HTTP from a test host discovering the real API controllers.
- Production database/SMTP were not contacted. No schema migration was applied.
