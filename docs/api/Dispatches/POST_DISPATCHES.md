# POST /api/v1/notifications/dispatches
Status: VERIFIED by automated tests, 2026-11-07.

## 1. Routing, authorization and scope
Public DispatchesController, paired with IDispatchService; follows current public inter-service REST policy. No new internal routes or service-key middleware. Caller supplies recipient UserId from its own business logic; this is a dispatch API, not a user-history endpoint. Existing Gateway/network policy controls access. Keep old in-app API unchanged for compatibility in this stage.
One template/channel per request; Push creates one delivery per snapshotted active device. Supported channels are Email, InApp and FCM Push; see POST_DISPATCHES_PUSH.md for per-device fanout. No SMTP/Push call in the request. Email deliveries are initially Pending and processed by DatabaseEmailWorker; InApp is completed immediately. This endpoint does not claim email was sent.

## 2. Request and validation
Application/Services/Dispatches/DispatchRequests.cs: CreateDispatchRequest.
Required non-empty SourceEventId and UserId GUIDs. TemplateCode required, max 150; Locale default vi-VN, max 20. Data: string-to-string placeholder map, max 50 entries; case-sensitive names such as OrderNo and TotalAmount. Destination optional valid email but required for an Email template. No client-supplied IsMandatory. No implicit User service lookup.
Example: {"sourceEventId":"a59264cc-b96d-4a36-9dca-34b50733cc49","userId":"781328b7-89e4-439c-a7f4-09627f1e8621","templateCode":"ORDER_PAID_IN_APP","locale":"vi-VN","data":{"OrderNo":"NEX-001","TotalAmount":"150000 VND"}}.

## 3. Response and idempotency
HTTP 200 pure DispatchResponse: NotificationId, DeliveryId, Channel, NotificationStatus, DeliveryStatus, IsReplay. PascalCase string states. Idempotency key is (SourceEventId, UserId, TemplateId), matching the one-template/channel contract. Repeat returns the first persisted result, including a previously skipped decision; it never creates a second delivery. Different template IDs can be used for the same source/user to request separate channels. Same-key changed data/destination is ignored in favor of the first persisted decision. Keep SourceEventId stable across retries.
Active template/config validation precedes replay; template removal may make a later replay unavailable. Global channel disabled always produces Skipped, including mandatory; mandatory bypasses user preferences only. No preference row means enabled. Optional user opt-out produces Skipped. Skipped requests are recorded as soft-hidden (IsDeleted=true, DeletedAt set) and marked read so existing history APIs do not display suppressed alerts. Replay ignores soft-delete filters and retains the original idempotency decision. NotificationStatus is Completed for InApp/Skipped, Pending for Email.

## 4. Errors and enum mapping
400 VAL_INVALID_INPUT: missing/empty IDs, fields or invalid destination/data limits.
404 TEMPLATE_NOT_FOUND: absent, inactive or deleted template.
409 CHANNEL_NOT_FOUND: missing/deleted configured channel.
400 CHANNEL_NOT_SUPPORTED: Sms is not implemented by this feature.
400 TEMPLATE_DATA_MISSING: required placeholder not supplied.
409 TEMPLATE_INVALID: malformed/empty template or rendered title/body exceeds persistence limits.
Errors use centralized ProblemDetails, English sentence case and stable codes.
Existing enum values are unchanged: Email=0, InApp=2; Pending=0/Sent=2/Skipped=4 (DeliveryStatus); Pending=0/Completed=1 (NotificationStatus).

## 5. Persistence, plan and tests
Single SaveChanges for notification, delivery and log. Add filtered unique index SourceEventId/UserId/TemplateId without deleting existing data. In a concurrent insert conflict, return the winner only if that exact idempotency key exists; otherwise propagate the DB failure. Seed ORDER_PAID_EMAIL mandatory=true and mandatory ORDER_PAID_IN_APP with deterministic GUID. Do not apply migration to production during this task.
Tests: mandatory bypass; optional opt-out/default; disabled/inactive/deleted config; missing data; HTML escaping and single-pass rendering; Email Pending; repeat and concurrent requests; atomic DB state; HTTP validation and Swagger. Record database limitations explicitly. Build/test Release before VERIFIED.


## 6. Verified implementation and handoff
- POST /api/v1/notifications/dispatches -> Controllers/Public/DispatchesController.cs -> Application/Services/Dispatches/IDispatchService and DispatchService.
- Request/response DTOs and single-pass TemplateRenderer live in the same Application feature folder.
- 20261107145450_AddTemplateDispatchIdempotency adds a filtered unique index, mandatory OrderPaid Email seed and mandatory OrderPaid InApp seed. Designer/snapshot generated together. Up does not drop tables/columns or delete data.
- Build Release: 0 errors, 0 warnings. Tests: 70 passed, 0 failed/skipped. Includes 3 PostgreSQL integration tests and a barrier-driven test forcing 8 concurrent inserts of the same key; exactly one notification/delivery/log persisted. HTTP tests cover actual MVC validation, replay, missing template, pure DTO and Swagger discovery.
- The test container was isolated and disposable. No configured project database or SMTP provider was contacted.
- EF scaffolding initially read stale Debug binaries with --no-build; the empty generated migration was removed and regenerated with --configuration Release from the design-time factory. No old migration was modified.
- DesignTimeDbContextFactory uses NOTIFICATION_DESIGNTIME_CONNECTION for explicit database commands; without it a dummy port-1 connection is used to allow safe scaffolding without reading runtime secrets.
- Pending Email is the initial API response; the database worker now sends asynchronously with retry. See ../../architecture/DATABASE_EMAIL_WORKER.md. Existing raw in-app endpoint behavior remains unchanged; migrate callers to template dispatch to use mandatory rules.
- GitNexus aggregate diff: CRITICAL, 26 tracked files / 85 symbols / 20 processes, including previous uncommitted email/ownership stage. Newly added files are also reviewed manually; graph counts are not the full feature inventory. No commit/deployment performed.

## 7. Before applying the migration to an existing database
The API currently auto-applies migrations at startup. Check duplicates before starting the updated API on an existing database. The following is a read-only preflight query; it was not run on the project database:

```sql
SELECT "SourceEventId", "UserId", "TemplateId", COUNT(*)
FROM "Notifications"
WHERE "SourceEventId" IS NOT NULL AND "TemplateId" IS NOT NULL
GROUP BY "SourceEventId", "UserId", "TemplateId"
HAVING COUNT(*) > 1;
```

Resolve any duplicates by an explicitly reviewed data migration before rollout; this task does not delete historical records automatically. Request data/destination on the same idempotency key does not override a previously stored decision.



## Push extension
FCM Push is implemented through the same dispatch endpoint and durable worker. DispatchResponse now adds deliveries; existing top-level delivery ID/status refer to the first ordered delivery and remain available. Push Destination must be omitted. See POST_DISPATCHES_PUSH.md and ../../architecture/FCM_PUSH.md. The latest full suite is 131 tests passed, none failed/skipped; earlier verification counts above describe prior stages.
