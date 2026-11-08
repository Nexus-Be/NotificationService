# POST /api/v1/notifications/seller-decisions

Status: VERIFIED — local automated tests,2026-11-08; source/provider rollout pending.

## 1. Routing, RBAC and responsibility
SellerDecisionsController → ISellerDecisionService, Services/SellerDecisions. POST plural route, service/admin trusted Gateway identity, anonymous401, buyer/seller/staff403;200 pure SellerDecisionResponse. Body16KiB/no-store. Staff reviews through source account service per UC-F6.6; this endpoint is delivery acceptance, not review authorization. Producer calls only after APPROVED+Seller role or REJECTED+reason transaction commits, supplies correct target/email. Notification does not grant role, create account/shop, approve products or prove source commit. Keep REST between services, no broker/gRPC. Do not call legacy direct seller-rejection/approval and this endpoint for one decision.

## 2. Strict input
Required nonempty sourceEventId,applicationId,userId GUIDs; email valid<=254,shopName required<=200,decision Approved/Rejected case-insensitive names only,decidedAt UTC<=serverNow+30sec; historical decisions accepted. Optional fullName<=200/default Customer,traceId<=100. RejectionReason<=1000, trimmed, required Rejected; Approved must omit/blank. Names/traces/control characters rejected; reason permits CR/LF/tab only among controls. Strict unknown JSON rejection: no arbitrary data/template/body/URL, identity documents, bank/CCCD/passport/password/OTP fields. Source must redact private document/bank details from the user-facing rejection reason; text is HTML encoded, not automatically PII-redacted.

Canonical email lower/trim,name/shop/reason trim,decision lower,timestamp UTC microseconds. Hash contains applicationId,target/email/name/shop/decision/reason/time, excludes trace and transport event ID. Stable sourceEventId must identify committed decision across source naming aliases (SellerRegistrationApproved/UserRoleAssigned or SellerRegistrationRejected/SellerOnboardingRejected). ApplicationId is the immutable reviewed request identity, not user ID. Resubmitted application must have new application ID; Notification does not infer re-review/version semantics.

## 3. Output, state and persistence
200: sourceEventId,applicationId,notificationId,deliveryId,notificationStatus,deliveryStatus,isReplay. New Email Pending is DB acceptance; worker Sent means SMTP accepted, never recipient read/role verification. Same event/canonical payload replay current state; different fields409 SELLER_DECISION_CONFLICT. Different event ID for same application409 SELLER_APPLICATION_ALREADY_NOTIFIED, even identical body; no second final decision/email. First valid accepted decision is immutable here; trusted source owns its correctness. Receipt/event/application keys remain reserved across soft deletion/template deactivation.

SellerDecisionDispatch: PK=sourceEventId,ApplicationId unique,RequestHash64,NotificationId unique restrictive FK,base timestamps/softdelete. No raw request/reason in receipt/audit; rendered user-safe reason in Email NotificationRecord history. Event advisory lock then application lock seed8011; template FOR SHARE ensures expected metadata until DispatchService creates immutable rendered record. Dispatch+receipt+accepted audit in one transaction. Generic dispatch with same event/template/user but no matching business receipt rejected409. No auto-delete/overwriting old outcomes.

Templates SELLER_APPROVED_EMAIL/SELLER_REJECTED_EMAIL vi-VN, active mandatory SecurityAlert5/Email0, IDs60000000-0000-0000-0000-000000000008/009. Approval explains existing account has Seller role/Portal access after commit; rejection includes required reason. Both encode FullName/ShopName/DecidedAtUtc, rejection additionally RejectionReason. No invented links or deadlines. User critical preferences cannot suppress; global Email disable yields Skipped and hidden record, replay never requeues automatically. Operator history/log/manual retry and DatabaseEmailWorker reused (3 default attempts,10/20seconds backoff,lease/fencing/strict SMTP). One Email record may appear in current inbox projection; no separate InApp/Push or multichannel grouping in this step.

## 4. Errors and enum mappings
400 VAL_INVALID_INPUT (including unknown JSON)/TEMPLATE_DATA_MISSING;401 UNAUTHORIZED;403 FORBIDDEN;404 TEMPLATE_NOT_FOUND;409 SELLER_DECISION_CONFLICT/SELLER_APPLICATION_ALREADY_NOTIFIED/SELLER_DECISION_TEMPLATE_INVALID/CHANNEL_NOT_FOUND/DELIVERY_STATE_INVALID/TEMPLATE_INVALID;413 REQUEST_BODY_TOO_LARGE;500 unexpected persistence failure without partial accepted state. English sentence-case ProblemDetails, no raw request echo.

NotificationType.SecurityAlert=5,ChannelType.Email=0;DeliveryStatus Pending0/Sending1/Sent2/Failed3/Skipped4;NotificationStatus Pending0/Completed1/Partial2/Failed3. API enum strings PascalCase. AuditAction.SellerDecisionAccepted=12 appended after0..11, acceptance target/decision/IDs/time/status only, no full reason/raw payload. Worker delivery results use existing NotificationLogs. No new persisted decision enum; request decision is validated text.

## 5. Pre-code execution and verification
1. Validate/normalize, compute canonical nonsecret SHA256 proof.
2. Transaction event lock → replay/conflict → application lock → reject previous final receipt.
3. Shared template row lock; require active Email/SecurityAlert/mandatory. DispatchService on same scoped DbContext/transaction; reject generic replay. Add receipt/audit, commit together.
4. Existing worker sends outside acceptance transaction; replay reads persisted status without extra sends/audit.
5. Additive migration table/3 logical constraints (PK/application/notification), FK and2 template inserts; no existing records modified. Target deployment must check seed ID/code+locale collisions before applying.
6. Real isolated PostgreSQL/Kestrel/Swagger tests, strict fake SMTP boundary through real DeliveryService; preserve274 regressions. Project DB/provider/source integration not touched.

- [x] Contract saved before C#.
- [x] Entity/config/seeds/migration.
- [x] Feature/controller/DI.
- [x] Tests/build/diff/GitNexus.
- [x] Handoff/history/checklists and temporary resources cleanup.

Limits: source must retry reliable REST with stable event/application IDs and correct user-safe decision after commit. Sender at-least-once around SMTP crash, no exactly-once claim. Global disabled channel can suppress mandatory notification by operator policy. Dedicated receipt metadata retention and source/live SMTP integration pending. Next business step: ban/unban, separately from Seller or temporary login lock.


## Verified example and contract handoff

POST example (replace IDs/time with actual committed reviewed application):

```json
{"sourceEventId":"35402ee2-7ae0-4533-af60-ef1b6256dc2c","applicationId":"9e412bcc-53c3-4247-b715-61faf0d465ef","userId":"7c6f20a0-2134-4e82-b116-c2771a989ff9","email":"owner@example.com","fullName":"Customer","shopName":"Example shop","decision":"Rejected","rejectionReason":"Please correct the submitted business details.","decidedAt":"2026-11-08T10:00:00Z","traceId":"seller-review-example"}
```

200 response contains sourceEventId/applicationId/notificationId/deliveryId/notificationStatus/deliveryStatus/isReplay. Initial statuses Pending/Pending; subsequent same-body POST uses same IDs/current persisted state and isReplay=true without requeue or new acceptance audit. Skipped remains Skipped after channel re-enable. Different event same application409 regardless matching body; do not retry with a new event ID. Future timestamp validation runs before replay; keep producer/server clocks aligned within30seconds. Approval sends decision Approved with no reason and only after the role is actually committed.

Service NotificationService; controller SellerDecisionsController; Application ISellerDecisionService/SellerDecisionService and CreateSellerDecisionRequest/SellerDecisionResponse in Services/SellerDecisions; Domain SellerDecisionDispatch; persistence SellerDecisionDispatchConfiguration and additive migration20261108101858_AddSellerDecisionNotifications. Existing DispatchService/DeliveryService reused without changing shared behavior. RBAC service/admin is sender policy, not Staff review permission. Request/response/error/enum tables above are authoritative.

Templates may be edited by authorized operators; required metadata active/mandatory/Email/SecurityAlert is checked under a shared row lock, original render remains stable after acceptance. Available data only FullName/ShopName/DecidedAtUtc and RejectionReason for rejected; asking for other data fails400 TEMPLATE_DATA_MISSING. Encoded content exceeding existing rendered4000-character budget fails409 TEMPLATE_INVALID rather than truncating the reason. Templates are not a mechanism to transmit sensitive documents.

Verification: Release build0 warnings/errors;295 passed,0failed/skipped (21 new seller cases,274 retained), no migration model drift. Real PostgreSQL/Kestrel/Swagger and real DeliveryService/fake strict sender, no real mail/source call. Migration only table/FK/2 unique indexes and2 template inserts; reviewed target rollout and duplicate seed ID/code+locale checks still required. Source integration, SMTP receipt,receipt retention pending. No project DB/credentials changes,commit or deployment. Plan .agents/plan/PLAN-20261108-seller-decisions.md; next ban/unban separately.
