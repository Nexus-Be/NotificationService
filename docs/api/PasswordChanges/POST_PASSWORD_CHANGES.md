# POST /api/v1/notifications/password-changes

Status: VERIFIED — local automated tests,2026-11-08; source/provider rollout pending. Feature: password changed/reset succeeded confirmation.

## 1. Routing and authorization
PasswordChangesController → IPasswordChangeService, feature Services/PasswordChanges. POST only,200 pure PasswordChangeResponse; role service/admin through trusted Gateway, anonymous401/other roles403. Body16KiB, no-store. User frontend calls source account service. Source must call only after password update commits, retry same sourceEventId on transport failure, never roll back account update because Notification is unavailable. Notification cannot independently prove the commit or recipient ownership; trusted producer supplies correct target/email.

## 2. Input and validation
Required nonempty sourceEventId and userId GUIDs; email required valid<=254; optional fullName<=200 defaults Customer; required kind Changed/Reset (case-insensitive text, no numeric enums); required changedAt UTC timestamp<=serverNow+30seconds. Historical events accepted without extending any OTP TTL. Optional traceId<=100; names/traces no controls. Trim email/name/kind; lowercase email and canonicalize kind; timestamps UTC microseconds. Unknown JSON fields rejected400 including password/newPassword/passwordHash/otpCode; no arbitrary template/body/data/link. No password/OTP in request, history, logs, inbox or response.

Example: sourceEventId,userId,email,fullName,kind="Changed",changedAt="2026-11-08T10:00:00Z",traceId optional. Kind Changed maps PASSWORD_CHANGED_EMAIL; Reset maps PASSWORD_RESET_SUCCEEDED_EMAIL, vi-VN fixed. Templates use FullName and ChangedAtUtc; encoded HTML. Separate subject/body confirms successful action and advises contacting Nexus support if unauthorized; no secrets or invented recovery links.

## 3. Response and persistence
200: sourceEventId,notificationId,deliveryId,notificationStatus,deliveryStatus,isReplay. New Email delivery Pending, never reports password verification/sent on queue acceptance. Existing durable worker handles provider results/retry/lease/fencing; Sent in its state model means SMTP accepted, not received/read. Operator delivery/log/retry APIs remain available. Global Email disable yields Skipped (and hidden notification), mandatory critical preference ignores user opt-out. No second channel created. Existing Email NotificationRecord can appear in current inbox projection; no separate InApp/Push grouping promised.

PasswordChangeDispatch: ID=sourceEventId,RequestHash64,NotificationId unique FK,CreatedAt/UpdatedAt/softdelete; hash is canonical normalized nonsecret payload including target,email,name,kind,changedAt, excludes transport trace. No raw request retained. Stable event ID reserved across target/kind, same canonical body replays current persisted delivery state, changed409. Ignore softdelete for receipt replay; receipt remains while retained. New accept uses advisory transaction lock by event ID, creates dispatch+receipt+AuditLog atomically. Replays do not re-render or depend on current template activation. Validate business template is active Email/SecurityAlert/mandatory; admin changes must not silently route security mail to another channel/type. Built-in codes reserved for this flow: do not use generic dispatch endpoint with the same sourceEventId/template/recipient before this API.

## 4. Errors and enum mappings
400 VAL_INVALID_INPUT (including unknown JSON),401 UNAUTHORIZED,403 FORBIDDEN,404 TEMPLATE_NOT_FOUND,409 PASSWORD_CHANGE_CONFLICT/PASSWORD_CHANGE_TEMPLATE_INVALID/CHANNEL_NOT_FOUND/DELIVERY_STATE_INVALID,413 REQUEST_BODY_TOO_LARGE;500 unexpected persistence failure with no partial acceptance. Existing TEMPLATE_DATA_MISSING/TEMPLATE_INVALID render validation may apply after operator edits. No secrets in errors.

NotificationType.SecurityAlert=5,ChannelType.Email=0; API strings PascalCase. DeliveryStatus Pending0/Sending1/Sent2/Failed3/Skipped4; NotificationStatus Pending0/Completed1/Partial2/Failed3. API values are PascalCase strings. AuditAction.PasswordChangeAccepted=11 appended after0..10; source target userId and trace are audit metadata, not authenticated actor proof. Worker attempts/results use existing NotificationLogs. No password-related enum introduced.

## 5. Pre-code plan and verification
1. Validate/normalize request; compute canonical SHA256 proof over nonsecret fields.
2. Begin transaction, take event-ID advisory lock, check receipt first and compare proof for replay/conflict.
3. Lock business template FOR SHARE and check shape; create via DispatchService on same scoped DbContext/transaction, ensure this event has not been separately dispatched through generic API; add receipt and acceptance audit, commit together.
4. Response from persisted notification/delivery; provider work after transaction through DatabaseEmailWorker. Replays read current state with no extra audit/send.
5. Add migration table/index/FK/two deterministic mandatory templates. Do not apply to project DB.
6. Real isolated PostgreSQL/Kestrel tests: strict boundary/auth/privacy, distinct content, mandatory/global disable, concurrency/replay/conflicts, rollback, migration/model and real DeliveryService with fake provider. Full Release regression preserves257 baseline tests.

- [x] Contract saved before code.
- [x] Entity/config/migration/seeds.
- [x] Feature/controller/DI.
- [x] Tests/build/diff verification.
- [x] Contract handoff/history/checklist and temporary resources cleanup.

Operational limits: User source integration and real SMTP verification pending. Durable queue is at-least-once at SMTP boundary; source must preserve event IDs. Dedicated receipt retention not implemented (EventLog cleanup does not apply). No project DB migration/provider calls/credentials edits/commit/deploy.


## Verified example and handoff

POST body (producer must replace IDs/timestamp with the committed account mutation):

```json
{"sourceEventId":"104fd351-5ad4-40bf-a1ee-924b371529de","userId":"1f2e6e5b-1fab-48ba-8204-f3e6a47bbfee","email":"account@example.com","fullName":"Customer","kind":"Reset","changedAt":"2026-11-08T10:00:00Z","traceId":"account-reset-success"}
```

Example200: sourceEventId,notificationId,deliveryId,notificationStatus="Pending",deliveryStatus="Pending",isReplay=false. Subsequent same-body POST returns the same IDs and current status with isReplay=true; it does not send again or reset retry attempts. A persisted Skipped response remains skipped on replay. A changed target/body with same event ID returns409. ChangedAt future validation runs before replay; producer/server clocks must remain within30seconds.

Contract handoff: NotificationService; POST route above; PasswordChangesController; IPasswordChangeService/PasswordChangeService; PasswordChangeDispatch entity; existing DispatchService/DeliveryService reused, no shared behavior changed. RBAC service/admin trusted Gateway only. Request and response in Services/PasswordChanges. All enum/error mappings above. Operator templates can change wording but cannot supply new runtime data fields; only FullName and ChangedAtUtc are available. Missing placeholders fail acceptance rather than accepting secret data. Do not call generic dispatch for these business events; use this endpoint exclusively. Protect Gateway headers/network boundaries; source must determine correct account/email after successful commit.

Schema rollout:20261108100603_AddPasswordChangeConfirmations and preceding migrations, normal reviewed deployment only. Two new codes/locale pairs and seed IDs must be free in target DB; no automatic delete/overwrite of customized records. Workers require existing SMTP configuration and DeliveryWorker enabled; permanent missing config becomes Failed, never simulated success. Current readiness pending-migration check covers this migration. No real SMTP receipt or source invocation tested.

Validation evidence: build Release0 warnings/errors;274 passed,0failed/skipped,17 new backend integration cases plus257 previous. Migration fully applied only in fresh isolated PostgreSQL test databases; HasPendingModelChanges=false. Real Kestrel/Swagger and DeliveryService with fake sender; concurrency, atomic rollback, template locking and status/error/privacy paths verified. Plan:.agents/plan/PLAN-20261108-password-success.md. Dedicated receipt retention/deployment/source/provider integration pending; Seller/account-state notifications are the next feature.
