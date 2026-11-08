# POST /api/v1/notifications/account-status-changes

Status: VERIFIED — local automated tests,2026-11-08; source/provider rollout pending.

## 1. Routing and responsibility
AccountStatusChangesController → IAccountStatusChangeService in Services/AccountStatusChanges. POST,200 pure AccountStatusChangeResponse; trusted Gateway service/admin principal,anonymous401,buyer/seller/staff403,16KiB/no-store. Staff executes actual ban/unban through source account API; notification caller RBAC does not authorize that decision. Source calls only after BANNED/ACTIVE commit and ban session revocation. Notification never mutates source status/roles/sessions,performs no automatic unban/deletion/login-lock action,and requires no login by the banned recipient to send email. REST only.

## 2. Strict request
Required sourceEventId,userId nonempty GUID;statusVersion positive long committed account-status revision;email valid<=254;change Banned/Unbanned case-insensitive names only;reason required trimmed<=1000 for both actions;changedAt UTC<=now+30seconds (historical allowed). Optional fullName<=200 defaults Customer,traceId<=100. Names/trace/email no controls;reason allows CR/LF/tab only among control chars. Unknown JSON members rejected: no passwords/OTP/bank/document/arbitrary payload/template/URL. Source must provide user-safe reason without private document details; encoding is not automatic PII redaction.

Canonical hash over userId/statusVersion,email lower/trim,name trim,change lower,reason trim,changedAt UTC microseconds; excludes trace/event ID. Source preserves sourceEventId and revision across transport retries and event naming aliases. A later ban/unban/reban gets a higher account-status revision. Notification accepts different revisions independently without guessing sequence/consecutive numbering/current status: delayed history retained, email states effective UTC time. The source must determine whether transition happened; this API cannot prove commit,revision,recipient ownership or current state.

## 3. Output, templates and persistence
200 fields sourceEventId,userId,statusVersion,notificationId,deliveryId,notificationStatus,deliveryStatus,isReplay. Pending is durable DB acceptance; worker Sent means SMTP accepted,not final receipt/read or evidence of account state. Same event/body replay reads persisted current delivery state even after template deactivation/receipt hiding; changed409 ACCOUNT_STATUS_CONFLICT. Different event same(userId,statusVersion)409 ACCOUNT_STATUS_ALREADY_NOTIFIED even when same body; prevents aliases sending twice. New revision supports new transition. Keys reserved across soft-delete while retained.

AccountStatusChangeDispatch PK=sourceEventId,UserId,StatusVersion,RequestHash64,NotificationId unique restrictive FK,base timestamps/softdelete;unique(UserId,StatusVersion). No raw request/full reason in receipt/audit; encoded user-facing reason in Email NotificationRecord history. Transaction event advisory lock then user+version lock seed8012; lock template FOR SHARE,validate active mandatory SecurityAlert5/Email0; call shared DispatchService on same DbContext/transaction and reject unmatched generic replay; receipt+audit atomic with dispatch/delivery/log. Replay no extra acceptance audit/send.

Templates ACCOUNT_BANNED_EMAIL/ACCOUNT_UNBANNED_EMAIL vi-VN,IDs60000000-0000-0000-0000-00000000000a/00b. Variables FullName,Reason,ChangedAtUtc only; encoded data,explicit effective time and distinct ban/unban wording,no invented duration/recovery URL/no mention new password/OTP. Banned means rule-violation suspension (not LOCKED_TEMP/deletion); Unbanned means source ACTIVE at stated time. Required reason in both per UC-F6.5 user-safe decision notice.

Existing strict DatabaseEmailWorker/retry/lease/fencing,operator delivery/log/manual retry reused. Critical preference cannot suppress; global disabled Email remains Skipped/hidden,replay cannot auto-requeue after re-enable. One Email record may appear in current owner inbox projection; no additional InApp/Push or multichannel grouping. At-least-once SMTP crash duplication remains possible.

## 4. Errors and mappings
400 VAL_INVALID_INPUT/TEMPLATE_DATA_MISSING;401 UNAUTHORIZED;403 FORBIDDEN;404 TEMPLATE_NOT_FOUND;409 ACCOUNT_STATUS_CONFLICT/ACCOUNT_STATUS_ALREADY_NOTIFIED/ACCOUNT_STATUS_TEMPLATE_INVALID/CHANNEL_NOT_FOUND/DELIVERY_STATE_INVALID/TEMPLATE_INVALID;413 REQUEST_BODY_TOO_LARGE;500 unexpected persistence failure without partial accepted state. English sentence-case ProblemDetails,no raw request echo. Rendered4000-character body limit may reject large encoded content409,not truncate reason.

NotificationType.SecurityAlert5;ChannelType.Email0;DeliveryStatus Pending0/Sending1/Sent2/Failed3/Skipped4;NotificationStatus Pending0/Completed1/Partial2/Failed3;API strings PascalCase. AuditAction AccountStatusChangeAccepted13 appended after0..12 (no existing value changed),metadata event/target/revision/change/time/IDs/status only,no full reason. Worker results in existing NotificationLogs. Change is validated DTO text,not new DB enum.

## 5. Plan, rollout and verification
Validate/normalize → event lock/replay → account-version lock/duplicate check → shared template lock/metadata guard → existing dispatch+receipt+accepted audit in one transaction → pure response → worker sends outside transaction. Same generic event/template/user without receipt rejected. Source should use this endpoint exclusively and map aliases to one stable event/revision.

Additive migration table/FK/2 unique indexes +2 deterministic template inserts,designer/snapshot; no existing data changed. Check target seed-ID/code+locale conflicts before reviewed deployment. No project DB migration/provider call/credentials edit/commit/deploy. Source REST integration,real SMTP receipt and receipt retention are separate pending work; EventLog retention does not remove receipts.

- [x] Contract before C#.
- [x] Entity/config/seeds/migration.
- [x] Feature/controller/DI.
- [x] Isolated PG/Kestrel/Swagger,real DeliveryService/fake provider,error/content/roles/privacy/idempotency/atomicity/ordering/retry tests;Release build/full295 regressions.
- [x] Handoff/history/checklist,diff/GitNexus,owned test resources cleanup.

Next feature: OrderPaid Buyer/Seller and bell logical-message grouping. This step does not implement that or auction Push.


## Verified example and handoff

POST body example (source must replace ID/revision/time with the committed mutation):

```json
{"sourceEventId":"cd930570-d7e7-4540-adac-362f63adbcca","userId":"a15ef314-c5a4-43a4-8471-20ec52ddd4ed","statusVersion":42,"email":"owner@example.com","fullName":"Customer","change":"Banned","reason":"Violation of marketplace rules.","changedAt":"2026-11-08T10:00:00Z","traceId":"account-ban-example"}
```

200 response includes sourceEventId/userId/statusVersion/notificationId/deliveryId/notificationStatus/deliveryStatus/isReplay; initial Pending/Pending. Same-body POST returns same IDs/current state with isReplay=true,no requeue or extra accepted audit. Changed body409. Different event ID same account/revision409 even same body; map source naming aliases to original stable ID/revision. Next transition must use its new committed account-status revision,not reuse this one. Revision need not be consecutive,belongs to that user,never created by Notification. Future time validation before replay requires producer/server clocks within30seconds. Historical events explicitly describe state as of changedAt and are not proof of current account state.

Service NotificationService;AccountStatusChangesController;IAccountStatusChangeService/AccountStatusChangeService;CreateAccountStatusChangeRequest/AccountStatusChangeResponse in Services/AccountStatusChanges;Domain AccountStatusChangeDispatch;configuration AccountStatusChangeDispatchConfiguration. Reuses existing DispatchService/DeliveryService without changing shared behavior. RBAC service/admin is notification-sender policy;Staff's source permission is checked by source,not granted by this endpoint.

Migration20261108104312_AddAccountStatusNotifications:table/FK/2 unique indexes and2 seed inserts,additive only. Apply through reviewed rollout after target seed ID/code+locale checks;no automatic merge/delete of customized templates. Existing readiness detects pending migration. Existing worker must be enabled and SMTP configured; missing configuration persists Failed,not simulated success. Dedicated receipt retention and actual User/SMTP integration pending. No project DB migration/live sends/credentials edit/commit/deploy.

Verification:Release build0 warnings/errors;317 passed,0failed/skipped,22 new account-status cases and295 retained. Isolated PostgreSQL/Kestrel/Swagger,model drift false,real DeliveryService with fake sender;reason encoding/privacy,roles/strict fields/size,replay/races/aliases/revisions/cycles/history,rollback,template lock/config,mandatory/global disable,retry/permanent failure verified. Plan .agents/plan/PLAN-20261108-account-status-changes.md. Next OrderPaid Buyer/Seller+bell grouping remains separate.
