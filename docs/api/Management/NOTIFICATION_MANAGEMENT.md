# Notification management REST API
Status: implemented and verified, 2026-11-07.

## 1. Routing and RBAC
NotificationManagementController -> INotificationManagementService in Application/Services/Management. Base /api/v1/admin/notifications. Existing GET at the base stays unchanged.

| Method | Suffix | Role | Result |
|---|---|---|---|
| GET | /templates | Admin, Staff | PagedData<TemplateResponse> |
| GET | /templates/{id} | Admin, Staff | TemplateResponse |
| POST | /templates | Admin | 201 TemplateResponse |
| PUT | /templates/{id} | Admin | TemplateResponse |
| GET | /channels | Admin, Staff | PagedData<ChannelResponse> |
| PUT | /channels/{code} | Admin | ChannelResponse |
| GET | /deliveries | Admin, Staff | PagedData<DeliveryResponse> |
| GET | /deliveries/{id} | Admin, Staff | DeliveryDetailResponse |
| GET | /deliveries/{id}/logs | Admin, Staff | PagedData<DeliveryLogResponse> |
| POST | /deliveries/{id}/retries | Admin | 202 RetryAcceptanceResponse |

All actions require non-empty Gateway user identity. Fix RequireRole so staff is permitted only when explicitly allowed. This does not add JWT verification or change the current Gateway trust boundary.

## 2. Inputs and validation
All request/response records are in the Management feature. Lists accept page>=1/pageSize 1-100 and return SQL Skip/Take/Count pagination. Template filters: code, channel, notificationType, isActive. Delivery filters: notificationId, userId, channel, status, from/to (UTC inclusive CreatedAt). Invalid enum names/numeric strings and reversed date ranges return VAL_INVALID_INPUT; offset overflow is rejected.
Create template: Code <=150, Locale <=20 (default vi-VN), NotificationType/ChannelCode names, SubjectTemplate <=500, BodyTemplate <=4000, IsMandatory, IsActive. Validate placeholder grammar. Existing non-deleted channel required. Code+Locale unique. OrderPaid/SecurityAlert/Otp must have IsMandatory=true. Updates require ExpectedUpdatedAt and mutable content/active/mandatory fields; code/locale/type/channel stay immutable.
Update channel: Name <=150, IsDisabled and ExpectedUpdatedAt. Never accept or return Config/credentials. Existing enum channel set is fixed; this does not add unsupported SMS transport.
Retry: RequestId (non-empty GUID), ExpectedRowVersion>0, Reason (non-blank, <=500). Actor comes from authenticated context, never request body. Push device is resolved by stored device ID and must still belong to the notification recipient.

## 3. Outputs and privacy
Pure DTOs, PascalCase enum names, camelCase JSON. Configuration timestamps returned for optimistic preconditions; updates advance timestamps at microsecond precision. Delivery reads expose masked email destination or a device/user ID hint, not raw tokens/email body/provider secrets. Log history exposes stable action messages and whitelisted attempt/error/retry/provider-message/request-ID metadata; never the raw metadata blob.
Retry response: RequestId, DeliveryId, AcceptedRowVersion, AcceptedAt, IsReplay. It describes the accepted retry request, not the latest worker state. Use GET delivery to read current state.

## 4. Errors, concurrency and idempotency
401 UNAUTHORIZED, 403 FORBIDDEN. 400 VAL_INVALID_INPUT / VAL_INVALID_TEMPLATE / MANDATORY_TEMPLATE_REQUIRED. 404 TEMPLATE_NOT_FOUND / CHANNEL_NOT_FOUND / DELIVERY_NOT_FOUND. 409 TEMPLATE_CONFLICT (duplicate code/locale), CONFIG_VERSION_CONFLICT (stale UpdatedAt), DELIVERY_VERSION_CONFLICT, DELIVERY_NOT_RETRYABLE, CHANNEL_DISABLED, DEVICE_TOKEN_INACTIVE and RETRY_REQUEST_CONFLICT.
Only Failed, visible template-backed Email/Push deliveries can be retried. Requeue resets attempts/error/last attempt, sets Pending and NextRetryAt now, increments RowVersion and makes parent Pending. Uses parent-before-delivery locks consistent with worker. Same request ID + same actor/delivery/expected version/reason replays original acceptance without resetting again; conflicting reuse returns 409. Preserve rendered content (no re-render); payload correction requires a new dispatch. Existing sending/accepted messages cannot be recalled, so exactly-once delivery is not promised.

## 5. Persistence and verification
Add DeliveryRetryRequests (GUID request ID PK, delivery FK Restrict, actor, reason, expected/accepted versions, previous attempt count, timestamps/soft-delete fields). Requests remain append-only, including replay over soft-hidden records. Add DeliveryRetry=7 to AuditAction without changing existing values. Store retry request, log and audit atomically with state updates. Read/action logs must exclude tokens/config secrets. New template/channel changes are audited in the same transaction. Additive migration only; no project DB changes during this task.
Test live MVC role filters (including Staff denied writes), field validation, pagination, strict enum inputs, template constraints, config version races, retry-vs-worker/concurrent request replay, cross-delivery key collision and rollback on failure using isolated PostgreSQL. Update docs/checklist after verified build/tests.

## 6. Request examples
Create a template: POST /api/v1/admin/notifications/templates (Admin, 201).

```json
{"code":"ORDER_SHIPPED_EMAIL_V2","locale":"vi-VN","notificationType":"OrderShipped","channelCode":"Email","subjectTemplate":"Order {{OrderNo}}","bodyTemplate":"Your order {{OrderNo}} was shipped.","isMandatory":false,"isActive":true}
```

Update mutable content: PUT /api/v1/admin/notifications/templates/{id} (Admin, 200). Copy expectedUpdatedAt exactly from the latest GET response; omitted required fields return 400, stale version returns 409.

```json
{"expectedUpdatedAt":"2026-11-07T16:00:00+00:00","subjectTemplate":"Order {{OrderNo}}","bodyTemplate":"Your order {{OrderNo}} is on its way.","isMandatory":false,"isActive":true}
```

Update a channel: PUT /api/v1/admin/notifications/channels/Email. Body: name (string), isDisabled (boolean), expectedUpdatedAt (timestamp copied from GET channels). Provider and Config are not writable.

Retry: POST /api/v1/admin/notifications/deliveries/{id}/retries (Admin, 202). Copy expectedRowVersion from current delivery detail. Reuse the same requestId/body after an HTTP timeout; use a new requestId for a later deliberate retry.

```json
{"requestId":"26ed1efe-cdf9-4269-ad65-4dcb6f0701f0","expectedRowVersion":7,"reason":"Provider credentials corrected"}
```

Response example:

```json
{"requestId":"26ed1efe-cdf9-4269-ad65-4dcb6f0701f0","deliveryId":"855a8a50-2a51-416d-bf10-cead3c81f2fb","acceptedRowVersion":8,"acceptedAt":"2026-11-07T16:00:00+00:00","isReplay":false}
```

List example: GET /api/v1/admin/notifications/deliveries?channel=Email&status=Failed&page=1&pageSize=20. Response: items + pagination (shared PagedData contract). DeliveryDetailResponse contains delivery, notificationStatus and templateCode; body/errorMessage/raw destination are omitted. GET templates includes content for authorized operators. TemplateResponse and ChannelResponse include updatedAt; DeliveryResponse includes rowVersion. Log responses include id/action/level/message/attempt/errorCode/nextRetryAt/providerMessageId/manualRetryRequestId/createdAt.

## 7. Enum persistence
Database enum columns remain integers, API outputs PascalCase strings. ChannelType: Email=0, Push=1, InApp=2, Sms=3 (Sms exists in the enum but this stage adds no SMS sender); NotificationType: OrderPaid=0, OrderShipped=1, AuctionOutbid=2, AuctionWon=3, ReputationPenalty=4, SecurityAlert=5, Otp=6, Promotion=7. DeliveryStatus: Pending=0, Sending=1, Sent=2, Failed=3, Skipped=4. Existing audit/log action integers are preserved; new AuditAction.DeliveryRetry=7. Filters use enum names and reject numeric strings.

## 8. Verified handoff
- Service: NotificationService; controller: NotificationManagementController; application: INotificationManagementService / NotificationManagementService.
- Entity: DeliveryRetryRequest; migration: 20261107163605_AddDeliveryRetryRequests (+designer/snapshot). Up adds only the table, index and Restrict FK; no existing data rewrite. Apply through the normal migration process after review; task did not migrate the project database.
- Release build: 0 errors, 0 warnings. Full suite: 143 passed, 0 failed, 0 skipped. ManagementApiIntegrationTests adds 12 isolated PostgreSQL/Kestrel tests covering auth, Swagger, validation, version conflicts, safe reads, retry concurrency/cross-delivery key collision, rollback and fake-worker completion/replay.
- Temporary PostgreSQL only, SMTP localhost and FCM/OAuth fakes in the overall suite; no live provider sends. Test database names are unique and each fixture drops only its own database. Management test host starts no real worker.
- Aggregate GitNexus detect-changes: CRITICAL across 36 tracked files / 99 symbols / 20 flows including previous stages; new untracked feature/migration/tests manually reviewed. No commit/deploy. Some older reference docs still contain historical contracts; this feature contract describes the implemented management endpoints.
- Not included: UI, credential CRUD, hard delete, provider delivery receipts, event-log API, retention or health work. Accepted retry is asynchronous and may still result in duplicate external delivery if the provider accepted an earlier attempt before a crash.
