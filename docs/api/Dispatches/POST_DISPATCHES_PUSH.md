# POST /api/v1/notifications/dispatches — Push extension
Status: VERIFIED by automated tests, 2026-11-07.

## 1. Routing and authorization
Same public DispatchesController/IDispatchService endpoint and trusted inter-service Gateway policy. Push recipients are selected only from active non-deleted DeviceTokens matching request.UserId; caller cannot pass a raw token or device ID as Destination. This stage does not enable internal-key middleware.

## 2. Request
Same CreateDispatchRequest. Use ORDER_PAID_PUSH with OrderNo/TotalAmount (mandatory) or AUCTION_OUTBID_PUSH with AuctionTitle/BidAmount (optional). Destination must be null/omitted for Push. Device Platform Web/Ios/Android means an FCM registration token from the corresponding Firebase client; APNs raw tokens or web subscription JSON are not supported here.
Snapshot at most 100 active owned devices. No active device: persist one Skipped delivery with PUSH_NO_ACTIVE_DEVICES. Optional opt-out/global channel off also persists Skipped and hides the notification from personal history. Mandatory bypasses user preferences, not missing devices/global channel disable.

## 3. Response and idempotency
Preserve existing notificationId, deliveryId, channel, notificationStatus, deliveryStatus, isReplay. Add deliveries: [{deliveryId,status}]. Top-level deliveryId/deliveryStatus refer to the first delivery ordered by ID; use deliveries for fanout, notificationStatus for aggregate state. Existing Email/InApp responses keep their original fields and add the single-item array.
One notification per SourceEventId/UserId/TemplateId and one Push delivery per snapshotted device. destination in storage is a DeviceToken.Id string, not its secret token. Replay returns all original deliveries and current states, including prior skipped decisions; no newly registered devices are added on replay. All writes are atomic under the existing notification unique key.

## 4. Errors and states
Existing errors retained. VAL_INVALID_INPUT for explicit Push Destination. TOO_MANY_DEVICES (409) if over 100 active devices; SMS remains CHANNEL_NOT_SUPPORTED. HTTP input/template errors remain centralized ProblemDetails. Provider errors are asynchronous delivery errors, not a failed REST transaction.
Push Pending -> Sending -> Sent / Pending retry / Failed / Skipped. Token ownership/revocation checked again per attempt. Only UNREGISTERED deactivates the exact device snapshot; INVALID_ARGUMENT can be payload-related and does not deactivate. No raw device tokens or access tokens in responses or logs. Push provider errors/FCM message name stored as sanitized metadata where applicable.

## 5. Plan and verification
Apply mandatory/channel/preferences before snapshot, render plain text, persist per-device Pending deliveries/logs. Generic delivery engine keeps lease/fencing and atomic aggregate state; raw device IDs must be resolvable to the same user. FCM uses OAuth ADC and HTTP v1, honors Retry-After; quota minimum retry 60s. Default attempt limit remains 3 total claims. Add seed-only migration; no destination width/schema change. Full Email/SMTP regression suite must remain passing. Test real PostgreSQL concurrent fanout/replay and token reassignment; fake FCM HTTP/token providers only.


## 6. Verification and handoff
Build Release: 0 errors/0 warnings. 131 tests passed, none failed/skipped, including actual PostgreSQL races and fake FCM HTTP/OAuth. Migration 20261107154659_AddPushNotificationTemplates was applied only to generated temporary test databases. No live Firebase credentials or external FCM requests used. Real-device setup remains an environment-dependent validation step; see ../../architecture/FCM_PUSH.md.

Example request:
```json
{"sourceEventId":"2cb688e1-8697-45f7-891d-031c502490e8","userId":"fe00ce4a-d2ce-4c85-88ca-3f6e708132f7","templateCode":"ORDER_PAID_PUSH","locale":"vi-VN","data":{"OrderNo":"NEX-001","TotalAmount":"150000 VND"}}
```

The response contains the original fields plus a deliveries array. Device tokens and device destinations are not exposed in this response. Any existing strict response-schema clients should allow the additional collection.
