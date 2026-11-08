# DELETE /api/v1/notifications/devices/{token}

Status: VERIFIED (local automated tests), 2026-11-07.

## 1. Routing and authorization
NotificationsController.RevokeDevice extracts authenticated caller identity with TryGetUserId and calls IUserNotificationService.RevokeDeviceAsync(userId, token). RequireRole also enforces identity in the MVC pipeline. Admin/Staff do not bypass ownership.

## 2. Request
Route token; no body. Identity is from Gateway-forwarded context, not a query parameter. No change to the existing trust boundary.

## 3. Response
204 NoContent. A missing, deleted, inactive or other-user token is an idempotent no-op with the same 204 response; the operation does not disclose who owns a token.

## 4. Errors and audit
401 UNAUTHORIZED when identity is missing. Actual mutation filters token + UserId + !IsDeleted; sets IsActive=false. AddAuditLog appends DeviceRevoke in the same SaveChanges, identifying the device by ID and never storing its raw token. Repeated/no-op revocations produce no additional audit row. AuditAction.DeviceRevoke = 6; all existing integer enum values remain unchanged. No schema migration.

## 5. Verification
- [x] Contract documented before C# changes in docs/api/API_EMAIL_OWNERSHIP_20261107.md.
- [x] Release build: 0 errors, 0 warnings.
- [x] Release tests: 47 passed, 0 failed, 0 skipped.
- [x] Swagger fetched over HTTP from a test host discovering the real API controllers.
- Production database/SMTP were not contacted. No schema migration was applied.

## Device lifecycle update (2026-11-08)
Current authoritative contract: docs/api/Devices/POST_DEVICES.md. Register/refresh/rotation and revoke share per-token transaction locks. Active foreign token409, inactive token can transfer after revoke; deleted token not revived. previousToken enables atomic rotation. Revoke never bypasses ownership, no-op204 if foreign/unknown/inactive; audit fingerprints only. Existing path-token values must be URL-encoded.237 tests pass, no project DB/provider changes or migration.
