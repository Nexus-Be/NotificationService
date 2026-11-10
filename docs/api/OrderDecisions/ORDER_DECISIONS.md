# Seller order decision notifications
Status: VERIFIED backend — 2026-11-08; source/client/provider integration pending.

## Scope/source boundary
SRS F1-08/F4-07:Seller accepts paid order→Shipping,or rejects/acceptance times out→Cancelled;refund/stock/reputation downstream belong to source services. Implement Notification notices for Buyer and Seller for Regular/Auction. REST only;not decision command,fulfillment/refund/penalty engine. Source confirms actual identities/order kind/ownership/paid and acceptance clock/final decision states before calling;Notification validates declared shape,not source truth. Shipping is source domain status,not proof of handover/carrier delivery. Separate payment and refund-success notices remain independent.

## API/input
POST /api/v1/notifications/order-decisions Service/Admin trusted Gateway,16KiB/no-store,200 OrderDecisionResponse,two logical items atomic. GET /{notificationId} authenticated owner-only,200 InAppNotificationItemResponse,no auto-read,anon401/foreign/deleted/non-feature404,Push-only detail allowed.
Required sourceEventId/orderId/shopId/buyerUserId/sellerUserId nonempty,Buyer!=Seller,orderNo nonblank<=100;OrderKind Regular/Auction case-insensitive. Auction requires AuctionId nonempty;Regular must omit/null AuctionId. Outcome Accepted/Rejected/AcceptanceTimedOut;required OrderStatus Shipping for Accepted,Cancelled for both others. Source maps only actual equivalent committed statuses,not pending decision. Required source PaidAt<=SellerAcceptStartedAt<=DecidedAt<=now+30s,SellerAcceptDeadline>SellerAcceptStartedAt. Accepted/Rejected DecidedAt<=deadline;Timeout DecidedAt>=deadline AND deadline<=serverNow. All normalized UTC microseconds,original deadline never re-derived24h/restarted on retry. Source owns24h notification-availability policy,eligibility and race at boundary;historical notices preserve times. Accepted Reason must be absent/null;Rejected/Timeout require user-safe nonblank Reason<=500. TraceId optional<=100,strings no controls. Unknown JSON rejects money/contacts/payment URLs/refund/stock/score flags/shipment secrets/password/OTP/token/private docs. No financial amount input;reason is source's user-safe shared explanation,not automatic PII redaction.

## Persistence/idempotency
OrderDecisionDispatch BaseEntity<Guid>,event PK,NotificationOrderKind Regular0/Auction1 int,OrderId,AuctionId nullable,hash64,BuyerNotificationId/SellerNotificationId;unique(kind,order),nullable auction unique,each notification unique,two restrictive FKs,softdelete. Event→kind/order→auction(if any) locks8022;two selected templates FOR SHARE sortedIDs. Atomic receipt/groups/deliveries/log/OrderDecisionAccepted23 audit(old0..22 stable,no reason/orderNo/raw input).
Canonical normalized IDs/kind/outcome/status/reason/UTC times,trace excluded. Same event/body replay current states;changed409 ORDER_DECISION_CONFLICT;new event same kind/order or auction409 ORDER_DECISION_ALREADY_NOTIFIED including hidden receipts. One initial Seller decision across all outcomes,corrections/reversals separate future workflows. Same GUID allowed for different kind in separate source tables. Fresh recreated auction/order uses new IDs. Does not require earlier paid receipt,erase prior history or independently reconcile contradictory source events;source must avoid generic duplicates.

## Channels/content/payload
Append NotificationType.OrderDecision12(old0..11 stable),AuditAction.OrderDecisionAccepted23. Six mandatory Push vi-VN templates ORDER_ACCEPTED_BUYER_PUSH600...01a/SELLER...01b,ORDER_REJECTED_BUYER_PUSH...01c/SELLER...01d,ORDER_ACCEPTANCE_TIMEOUT_BUYER_PUSH...01e/SELLER...01f. Push mandatory,InApp optional unless own mandatory pair,global disable wins. Default matrix39 pairs/13mandatory(previous36/12),explicit fixture counts only;no common worker or critical policy change. No Email. One logical item per recipient,Push devices share own parent ID;no/over100 devices skip affected Push only,allowed inbox retained,Push-only active/all skipped hidden,provider failure/retry isolated. Plain parent snapshots unchanged on retry/template edit.
Accepted text records Seller accepted source outcome/time and asks check current order,no handover/tracking/delivery promise. Rejected/timeout text records cancellation reason/time and asks check current order/refund status;never says funds refunded,points deducted/stock released/fine paid. No shipping/monetary claims inferred. Payload type OrderDecision,audience Buyer/Seller,orderKind/orderId/shopId/auctionId nullable/orderNo/outcome/orderStatus,source decision/acceptance clock timestamps and reason nullable;no other user identity/contact/price. Clients safely render text and enforce target current permissions/state.

## Response/errors/enums
Response sourceEventId,orderId,isReplay,buyer/seller each notificationId,userId,notificationStatus,inboxVisible,deliveries[id,channel,status,errorCode]. Pending=durable queue,InApp Sent=persisted,Push Sent=provider accepted not read/actual source status proof.
400 VAL_INVALID_INPUT/TEMPLATE_DATA_MISSING;401 UNAUTHORIZED;403 FORBIDDEN POST;404 TEMPLATE_NOT_FOUND/NOTIFICATION_NOT_FOUND;409 ORDER_DECISION_CONFLICT/ORDER_DECISION_ALREADY_NOTIFIED/ORDER_DECISION_TEMPLATE_INVALID/CHANNEL_NOT_FOUND/TEMPLATE_INVALID/DELIVERY_STATE_INVALID;413 REQUEST_BODY_TOO_LARGE;persistence500 rollback. Skips CHANNEL_DISABLED/PREFERENCE_DISABLED/PUSH_NO_ACTIVE_DEVICES/PUSH_DEVICE_LIMIT.
NotificationType.OrderDecision12,NotificationOrderKind Regular0/Auction1,AuditAction.OrderDecisionAccepted23;Email0/Push1/InApp2,ScopeAll0;DeliveryStatus Pending0/Sending1/Sent2/Failed3/Skipped4;NotificationStatus Pending0/Completed1/Partial2/Failed3;PascalCase API.

## Plan/context/impact/verification
Follow existing AGENTS/rules/specs,new-feature-endpoint/service-api-doc/controller-conventions workflows. Read SRS F1/F4,OrderPayment/AuctionOrderPayment and SellerResult features/entities/config/tests. Pre-edit DbContext MEDIUM/interface+seed LOW,enums/audit/DI UNKNOWN manually followed,index stale one commit. Manual significant source state/clock/refund wording/privacy risks disclosed. No common worker/preference critical policy changes.
- [x] Contract/plan before C#.
- [x] Receipt/config/type12/kind enum/audit23/DbSets/DI,DTO/service/controller/6templates.
- [x] Additive migration/designer/snapshot:table/2FKs/4unique indexes/6seeds.
- [x] PG/Kestrel tests six kind/outcome combinations,strict conditional input/states/times/auth/size,role privacy/grouping,channel/prefs/device/owner/read/worker,replay/aliases/races/rollback/template locks/model/Swagger/history.
- [x] Full Release0errors/warnings/full551 regression,Git diff/GitNexus,docs/handoff/checklists,owned PG cleanup.

Rollout migration/check seed IDs/code+locale collision and type12/kind/audit23 reader upgrades before source enable. Source/client/FCM/receipt/latency/retention remain pending;Sent=provider accepted,crash duplicates possible. No project DB/credentials/live sends/Git index/commit/deploy. Next backend:refund result notification with actual source confirmation,then fulfillment events;not inferred from cancellation.


## Seller order decisions — 2026-11-08

Implemented POST /api/v1/notifications/order-decisions(Service/Admin,16KiB/no-store,200 OrderDecisionResponse),GET /{notificationId}(authenticated owner,no auto-read,anonymous401/foreign/deleted/non-feature404). SRS F1-08/F4-07,Regular/Auction×Accepted/Rejected/AcceptanceTimedOut. Source confirms paid order and Seller decision resulting in Shipping(accepted) or Cancelled(rejected/timeout). Shipping is domain state,not proof of physical handover. Required linked event/order/shop/Buyer/Seller,distinct parties,OrderKind,conditional AuctionId,orderNo,source PaidAt/acceptance-clock/deadline/DecidedAt;paid<=clockStart<=decision<=now+30s,deadline>clockStart,accepted/rejected decision<=deadline,timeout decision>=deadline AND deadline<=now. Rejected/timeout user-safe reason<=500 required;accepted reason absent. Unknown money/contact/secret/refund/stock/shipment/points flags rejected. Notification validates shape,not source ownership/payment/decision truth,does not accept/cancel/ship/refund/penalize. Source owns24h policy,notification availability clock,eligibility/races and durable immutable REST retry;historical notice retains source timestamps and never resets deadline. Reason user-safe source requirement,not automatic PII sanitization.

OrderDecisionDispatch event PK,NotificationOrderKind Regular0/Auction1 int,OrderId/nullable AuctionId,hash64,Buyer/Seller notification FKs/softdelete;unique(kind,order),nullable unique auction,unique each notification. Event→kind/order→auction locks8022,selected templates FOR SHARE sortedIDs;receipt/two logical items/all deliveries/log/OrderDecisionAccepted23 audit atomic(old0..22 stable,no raw reason/orderNo/request). Same normalized event replay current state,changed409 ORDER_DECISION_CONFLICT,new event alias reusing kind/order or auction409 ORDER_DECISION_ALREADY_NOTIFIED even hidden. One immutable initial decision across outcomes;corrections/reversals separate future workflow. Same source GUID in Regular/Auction tables allowed by composite key;new recreated orders fresh IDs. Earlier payment history retained;no need paid-notification receipt to accept source-confirmed outcome. Source must avoid generic duplicate and reconcile any contradictory events itself.

Append NotificationType.OrderDecision12(old0..11 stable),six mandatory Push vi-VN templates ORDER_ACCEPTED_BUYER/SELLER_PUSH600...01a/01b,ORDER_REJECTED_BUYER/SELLER_PUSH...01c/01d,ORDER_ACCEPTANCE_TIMEOUT_BUYER/SELLER_PUSH...01e/01f. Push mandatory,InApp optional unless own mandatory pair,global disabled wins. Effective matrix39 pairs/13mandatory(previous36/12),explicit count fixtures updated. No common worker/critical preference policy change or Email. Two logical items,per-device own parent ID,device limit/no tokens skips affected Push only;allowed inbox/sibling preserved,Push-only active/all suppressed hidden. Retry keeps plain parent snapshot. Content accepted source outcome or cancelled reason/time,asks check current order/refund status;never asserts refund completed,points deducted,stock released or actual carrier handover. Payload type OrderDecision,audience,kind/order/shop/auction/orderNo/outcome/status/source times/reason,no other user/contact/price. Safe text rendering and current target permissions/state required in client.

Migration20261108150243_AddOrderDecisionNotifications adds table/2restrictive FKs/4unique indexes/6seeds,integer kind enum;Up additive,no old data mutation,designer/snapshot synchronized. Applied only to isolated test databases. Review migration/template-ID/code+locale collisions,upgrade type12/kind/audit23 API/SDK/worker readers before source enable. Real source/client/provider receipt/latency and retention remain pending;Sent only provider accepted,provider crash duplicates possible. No project DB/credentials/live sends/Git index/commit/deploy changes.

Release0 errors/warnings;583 tests passed,0failed/skipped(551 retained+32 new PG/Kestrel cases with real DeliveryService/fake provider). Covers six business combinations/clock/boundaries/strict fields/auth/size/privacy/grouping,mandatory Push/optional InApp/global/device/owner/read,worker failure/retry/revocation,canonical replay/hidden keys/order kinds/alias and competing outcomes/recipient races,atomic rollback/template errors/shared lock/generic collision/corrupted manifest/model/Swagger and retained regular+auction payment history. First targeted31passed,history case added then included in full suite. Pre-edit DbContext MEDIUM/interface+seed LOW/enums+DI UNKNOWN manually followed. Final GitNexus aggregate HIGH(15trackedfiles/23symbols/14processes,index stale one commit),includes all outstanding stages and DbContext/SaveChanges/model/enum reader paths;report flags shifted existing members as well as additions,shared SaveChanges implementation and worker not changed in this step. Reviewed source and full regression;new files/framework manually followed. HIGH deployment-reader compatibility remains explicit,not treated as low. Owned temporary PG stopped after final verification. Handoff docs/api/OrderDecisions/ORDER_DECISIONS.md. Next backend:confirmed refund result,then fulfillment outcomes;other cancellation reasons/order completion remain pending.


## Example and handoff

Synthetic historical cancellation after Seller rejection;not evidence of completed refund. Auction requires actual auctionId and orderKind Auction. Accepted requires Shipping and absent reason;timeout requires Cancelled and decision on/after expired deadline.
```json
{
  "sourceEventId": "11111111-1111-1111-1111-111111111111",
  "orderId": "22222222-2222-2222-2222-222222222222",
  "shopId": "33333333-3333-3333-3333-333333333333",
  "buyerUserId": "44444444-4444-4444-4444-444444444444",
  "sellerUserId": "55555555-5555-5555-5555-555555555555",
  "orderKind": "Regular",
  "outcome": "Rejected",
  "orderStatus": "Cancelled",
  "orderNo": "ORDER-DEMO-001",
  "reason": "Seller cannot fulfill this order.",
  "paidAt": "2026-11-06T05:00:00Z",
  "sellerAcceptStartedAt": "2026-11-06T05:01:00Z",
  "sellerAcceptDeadline": "2026-11-07T05:01:00Z",
  "decidedAt": "2026-11-06T07:00:00Z"
}
```

Controller: Notification.Api/Controllers/Public/OrderDecisionsController.cs. Application: Notification.Application/Services/OrderDecisions/. Entity: Notification.Domain/Entities/OrderDecisionDispatch.cs. Persistence: Notification.Infrastructure/Persistence/. Tests: Notification.Tests/OrderDecisionApiIntegrationTests.cs. Request/response/RBAC/errors/enums above define handoff. Source keeps IDs/outcome/reason/clock immutable and retries after actual commit. Client routes by orderKind and owner notificationId,enforces current target permissions/state. Do not use this notice as refund or shipment completion evidence.
