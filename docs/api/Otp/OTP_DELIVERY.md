# OTP registration/password-reset delivery
Status: VERIFIED — local automated tests,2026-11-08; source/provider rollout pending.

## API and responsibility
POST /api/v1/notifications/email/otp remains path/HTTP200 pure OtpEmailDispatchResponse. Require authenticated Gateway principal with role service/admin; anonymous401, other roles403. User frontend calls User Service, not Notification directly. User Service validates account/OTP/limits and never uses status as verification proof. GET /api/v1/notifications/otp-dispatches/{id} same role policy, pure masked status200/404. Public/internal key middleware unchanged; callers must migrate identity headers through trusted Gateway before rollout. Body limit16KiB, no-store. No change to general email/rejection routes in this step.

Request existing email/fullName/otpCode/expiresInMinutes/purpose retained and bounded (email254,name200,code32 nonblank/no whitespace/control,purpose registration/password_reset/seller_registration). Add requestId(Guid? optional nonempty),userId(optional nonempty target),issuedAt/expiresAt(both or neither),traceId<=100. Explicit UTC normalized microsecond validity: expires>issued,TTL<=60min,issued<=now+30seconds. New requests already expired400; replay returns existing delivery status even later expired. Old fields use accepted server time+expiresInMinutes1..60; missing ID assigned GUID, reliable retry requires supplied ID. Expiry default derives persisted first record on replay. No secret echo.

Response existing recipient/fullName/expiresInMinutes/purpose/status/acceptedAt retained; append requestId,expiresAt,isReplay. Durable statuses Queued/Sending/ProviderAccepted/Failed/Expired/Superseded. Queued means database accepted, ProviderAccepted SMTP accepted, neither means account verified or user read mail. GET returns requestId,maskedRecipient,purpose,status,attemptCount,issuedAt,expiresAt,nextRetryAt,errorCode,createdAt/updatedAt; no fullName/code/ciphertext/fingerprint. Existing standalone non-durable constructor fallback remains test/legacy compatibility and is not runtime DI.

## Idempotency and resend
Request ID primary key. Protect canonical fingerprint containing normalized recipient/name/code/purpose/user/issued/expiry/relative-minutes; no plaintext low-entropy OTP hash stored. Same request metadata/code replays; changed body409 OTP_REQUEST_CONFLICT. Missing/unreadable proof503 OTP_PROTECTION_UNAVAILABLE. Trace transport hint excluded from fingerprint.
Advisory group lock on normalized email+purpose serializes new accept/worker claim/finalize. New different ID issuedAt must be strictly later than latest group record, else409 OTP_REQUEST_OBSOLETE. Supersede Queued/Sending earlier group rows atomically, clear payload and bump version. Group-independent purpose isolation. User Service must issue fresh ID/timestamp on resend and invalidate old code; Notification cannot revoke email already in flight.

## Persistence, encryption, audit
OtpEmailDispatches: GUID PK,recipient254,recipientKey254,purpose40,userId?,fullName200,issuedAt/expiresAt,relative minutes,protectedPayload(varchar16000 nullable),protectedFingerprint(varchar1000 required),status int,attemptCount,rowVersion concurrency,long,lastAttemptAt/nextRetryAt?,errorCode100,traceId100?,base timestamps/soft-delete. Index group(recipientKey,purpose,issuedAt), scheduling(status,nextRetryAt), expiry. No OTP in Notifications/EventLogs/AuditLogs.
DataProtection purposes separate payload/fingerprint, stable application name; optional OtpProtection:KeyRingPath. Persist/share key ring across instances/restarts and protect key material outside project DB. Defaults use platform key store; ephemeral containers must mount persistent directory. Terminal payload is cleared, protected fingerprint retained for replay; payload decrypt failure Failed OTP_PAYLOAD_UNAVAILABLE. No raw exception messages stored.
AuditAction adds OtpAccepted8/OtpAttempt9/OtpResult10 (existing0..7 unchanged). Audit uses AddAuditLog same transaction with recipient/purpose/ID/attempt/status/time/code only; userId is source-provided target account when supplied, traceId source hint. No code/body/request/ciphertext/fingerprint in audit or logs. Mask recipient on status API. OTP metadata retention separate from EventLogs cleanup; terminal payload removal is immediate in this implementation.

## Worker
New DurableOtpWorker replaces old RAM worker in production DI, fresh scope per iteration, existing OtpEmail health name. OtpWorker settings poll1sec(1..30),sendTimeout30sec(1..120),lease120sec(>sendTimeout+10,<=600),maxAttempts3(1..3),baseDelay5sec(1..60). Lease recovery after shutdown/crash; claim/finalize take group advisory lock and row fence/version. Expired jobs skip before provider call; each attempt audited. Send outside transaction via strict SMTP boundary/stable MessageId. Retry only transient result within attempt/expiry budget; payload erased at terminal. Send timeout is min(configured,remaining lifetime). Stop cancellation leaves Sending for lease recovery. Late/stale completion after supersession/reclaim cannot mutate current state. SMTP may duplicate after crash, no exactly-once claim.
Content distinct registration/password_reset/seller_registration, HTML-encoded name/code and absolute UTC expiry. No rawOTP logs. Health active budget accounts for send timeout/grace; durable waiting budget=poll+grace, unlike retained standalone RAM idle behavior.

## Errors and verification
400 VAL_INVALID_INPUT/OTP_EXPIRED;401 UNAUTHORIZED;403 FORBIDDEN;404 OTP_DISPATCH_NOT_FOUND;409 OTP_REQUEST_CONFLICT/OTP_REQUEST_OBSOLETE;503 OTP_PROTECTION_UNAVAILABLE;413 REQUEST_BODY_TOO_LARGE. Provider errors persisted as safe async codes, not failed acceptance HTTP transaction.
Additive migration/designer/snapshot only. Test isolated PostgreSQL/real Kestrel, ephemeral test protection, fake strict sender, concurrency/replay/rollback/resend/stale owner/lease/expiry/retry/secret clearing/role/status/privacy/health. Preserve237 regressions with caller fixture adjustments. No real User integration/SMTP/provider credentials/project DB/deployment. Password-change confirmation and other business groups remain next steps.


## Example and handoff

Service: NotificationService. Controller: EmailController and OtpDispatchesController. Application: IOtpEmailDispatchService/OtpEmailDispatchService, EmailNotificationService runtime delegation. Entity: OtpEmailDispatch; enum OtpDispatchStatus integer0..5. Infrastructure: DataProtectionOtpPayloadProtector, DurableOtpWorker, strict SmtpEmailDeliverySender. RBAC: service/admin trusted Gateway identity, no direct frontend call.

POST body (replace times with a current challenge; never log the real code):

```json
{"requestId":"a1475527-2918-407c-98d0-9f6640dd6f21","userId":"9ce8e1a4-bc7d-4bd2-b98f-f9bebac9cf8b","email":"user@example.com","fullName":"Customer","otpCode":"example-only","purpose":"password_reset","issuedAt":"2026-11-08T10:00:00Z","expiresAt":"2026-11-08T10:05:00Z","expiresInMinutes":5,"traceId":"user-reset-example"}
```

200 response retains prior fields and adds requestId/expiresAt/isReplay. GET status reveals maskedRecipient, status and safe attempt/error/time metadata only. Error codes and enum mappings are above; model-binding/body-limit errors use existing global ProblemDetails handling.

Migration: 20261108095031_AddProtectedOtpDispatches (+designer/snapshot), additive table/3 indexes only. Apply through normal deployment after review; never clear pending OTPs to resolve a key problem. Direct .NET configuration uses OtpProtection__KeyRingPath; Compose mounts notification-otp-keys at /var/lib/notification/otp-keys. Preserve existing key ring during upgrades, share between replicas, restrict access and protect backup/key material. Disk persistence alone does not encrypt keys at rest on every platform. Do not copy production keys into tests.

Verification: Release build0 warnings/errors;257 passed,0failed/skipped (20 new OTP/host,237 retained). HTTP roles/no-store/Swagger and validation; encrypted DB/audit/status privacy; idempotency and cross-group conflict; competing workers, resend/expiry/retry/timeout/permanent failure; cancellation/lease/fences and rollback; persisted key ring restart/purpose separation and unreadable-key failure; worker scopes/health failure/recovery/stop. SMTP is faked for new OTP tests; previous local SMTP/FCM regressions preserved. Project database not migrated; no actual User Service/provider/client integration or deployment. Plan: .agents/plan/PLAN-20261108-otp-basic-flow.md.
