# Commerce-confirmed order completion notifications
Status: VERIFIED backend — 2026-11-08; Commerce/client/provider integration pending.

## Scope/source boundary
SRS F1-11/12 and F4-11:Commerce confirms Completed after carrier Delivered;Buyer sees completion/review options,Seller sees order completion. SRS permits completed order with failed Seller-wallet credit and leaves auction transfer timing open. This endpoint does not assert money credited/reputation granted or start reviews unconditionally. Seller settlement notification is a separate next workflow after confirmed ledger credit. REST only. Source owns actual completed/delivered/paid truth,recipient/order/shop/auction/shipment linkage,dispute eligibility and durable immutable retry. Notification validates declared shape,not source truth,never completes orders,confirms physical receipt,pays/refunds/credits wallets,grants reputation/review permission or contacts GHN.

## API/input
POST /api/v1/notifications/order-completions Service/Admin trusted Gateway,16KiB/no-store,200 OrderCompletionResponse;two logical items Buyer/Seller accepted atomically. GET /{notificationId} authenticated owner,200 InAppNotificationItemResponse,no auto-read,anonymous401/foreign/deleted/non-feature404,Push-only active detail allowed.
Required nonempty sourceEventId,orderId,shopId,shipmentId,buyerUserId,sellerUserId;Buyer!=Seller. OrderKind Regular/Auction case-insensitive;Auction requires nonempty auctionId,Regular omit/null. OrderNo nonblank<=100/no controls. Required OrderStatus Completed,DeliveryStatus Delivered(case-insensitive). Required PaidAt<=DeliveredAt<=CompletedAt<=now+30sec,UTC microseconds. Historical completion allowed,never reset source times;no Buyer confirmation click field required. TraceId optional<=100/no controls. Strict unknown JSON rejects payout/net amount/fees/currency/ledger/wallet credited/balance/reputation/review URL/contact/private docs/password/OTP/token. No financial amount or arbitrary URL accepted. Source must resolve actual completion/dispute/identity facts first;absence of Notification shipping/paid receipt does not block verified source event.

## Persistence/idempotency
OrderCompletionDispatch BaseEntity<Guid>,event PK,OrderKind Regular0/Auction1 int,OrderId,nullable AuctionId,ShipmentId,hash64,Buyer/Seller notification IDs,softdelete. Unique(kind,order),nullable unique auction,unique each notification;two restrictive notification FKs. Event→kind/order→auction(if present) advisory locks8025;two templates FOR SHARE sortedIDs. Receipt/groups/deliveries/log/OrderCompletionAccepted26 audit atomic(old0..25 stable,no orderNo/body/raw input). Same normalized event/body replay current branch state;changed409 ORDER_COMPLETION_CONFLICT,new event reusing kind/order or auction409 ORDER_COMPLETION_ALREADY_NOTIFIED including hidden receipts. One immutable final completion per source order;new/recreated orders use new IDs,corrections/reversals separate events. Different source tables can share order GUID across kinds;source owns actual shared shipment linkage. Independent of shipment Delivered/payment/decision notices,does not delete history or reconcile contradictory source events. Avoid generic dispatch duplication.

## Channels/content/payload
Append NotificationType.OrderCompleted14(old0..13 stable),AuditAction.OrderCompletionAccepted26. Two mandatory Push vi-VN templates ORDER_COMPLETED_BUYER_PUSH600...033 and ORDER_COMPLETED_SELLER_PUSH600...034. Push mandatory,InApp optional unless own mandatory pair,global disable wins. Matrix45 pairs/15mandatory(previous42/14),explicit count fixtures updated;no Email/common worker/SaveChanges/critical policy change. One item each,per-device own parent,missing/over100 devices skip affected Push only,allowed inbox/sibling preserved;Push-only active/all skipped hidden. Retry preserves plain parent/source-time snapshot.
Buyer text records completed at source time,asks view current order and available review options;does not promise review eligibility/reputation increased. Seller text records completion,asks check current order and Seller settlement status;does not claim wallet credited/current balance/funds withdrawable/commission amount or completed bank transfer. Payload type OrderCompleted,audience Buyer/Seller,orderKind/orderId/shopId/auctionId nullable/shipmentId/orderNo,paidAt/deliveredAt/completedAt,OrderStatus Completed/DeliveryStatus Delivered;no other user identity/contact or money/ledger/review URL. Client safely renders plain text and checks current authorized order/review/settlement APIs. Sent only provider accepted,not read/financial verification.

## Responses/errors/enums
Response sourceEventId,orderId,isReplay,buyer/seller each notificationId,userId,notificationStatus,inboxVisible,deliveries[id,channel,status,errorCode]. Pending durable queue,InApp Sent persisted.
400 VAL_INVALID_INPUT/TEMPLATE_DATA_MISSING;401 UNAUTHORIZED;403 FORBIDDEN POST;404 TEMPLATE_NOT_FOUND/NOTIFICATION_NOT_FOUND;409 ORDER_COMPLETION_CONFLICT/ORDER_COMPLETION_ALREADY_NOTIFIED/ORDER_COMPLETION_TEMPLATE_INVALID/CHANNEL_NOT_FOUND/TEMPLATE_INVALID/DELIVERY_STATE_INVALID;413 REQUEST_BODY_TOO_LARGE;persistence500 rollback. Skips CHANNEL_DISABLED/PREFERENCE_DISABLED/PUSH_NO_ACTIVE_DEVICES/PUSH_DEVICE_LIMIT.
NotificationType.OrderCompleted14,NotificationOrderKind Regular0/Auction1,AuditAction.OrderCompletionAccepted26;Email0/Push1/InApp2,ScopeAll0;DeliveryStatus Pending0/Sending1/Sent2/Failed3/Skipped4;NotificationStatus Pending0/Completed1/Partial2/Failed3;API PascalCase.

## Plan/context/impact/verification
Read AGENTS/rules/specs,new-feature-endpoint/service-api-doc/controller-conventions workflow,SRS F1-11/12/F4-11,decision/shipment/refund features/entities/config/DTO/tests. Pre-edit DbContext MEDIUM/interface+seed LOW/enums/audit+DI UNKNOWN manually follow framework/readers,prior aggregate HIGH(index stale/outstanding stages) not treated low. Significant source completion/financial/review wording risk disclosed. No changes to prior shipping/payment/decision logic.
- [x] Contract/plan before C#.
- [x] Receipt/config/type14/audit26/DbSets/DI,DTO/service/controller/2templates.
- [x] Additive migration/designer/snapshot:table/2FKs/4unique indexes/2seeds.
- [x] PG/Kestrel tests both kinds,status/time/auth/strict fields/size/privacy/grouping,channel/preferences/device/owner/read/worker/retry,replay/aliases/races/hidden keys/rollback/template lock/model/Swagger and prior Delivered history retained.
- [x] Full Release0errors/warnings/full640 regression,scoped diff/GitNexus,docs/handoff/checklists,owned PG cleanup.

Rollout migration/seed-ID+code-locale collision checks and type14/audit26 API/SDK/worker/client readers before source enable. Source/Commerce/client/FCM receipt/latency/retention remain pending;provider crash duplicates possible. Global unrelated whitespace/formatting changes from other steps preserved. No project DB/credentials/live sends/Git index/commit/deploy. Next backend:confirmed Seller-wallet settlement notification,not inferred from completion.


## Commerce-confirmed order completion — 2026-11-08

Implemented POST /api/v1/notifications/order-completions(Service/Admin,16KiB/no-store,200 OrderCompletionResponse),GET /{notificationId}(authenticated owner,no auto-read,anonymous401/foreign/deleted/non-feature404). SRS F1-11/12/F4-11,Regular/Auction:two logical Buyer/Seller items after actual Commerce Completed. Strict linked event/order/shop/shipment/Buyer/Seller IDs,distinct users,kind/conditional auction,orderNo,declared Completed+Delivered and paid<=delivered<=completed<=now+30s,UTC microseconds. Unknown financial/ledger/wallet/reputation/review URL/contact/secret fields rejected. No Buyer confirmation click needed. Source verifies paid/delivery/completion/dispute eligibility and correct linked recipients;Notification validates shape,does not complete orders/pay/credit wallets/grant reviews or reputation. Historical snapshot preserves completion time and requires current authorized target checks. Prior shipment/payment receipt not required;independent history retained.

SRS ordinary order completion may precede successful Seller credit;auction transfer timing remains unspecified. Buyer copy records completion and available review options,not unconditional review/reputation entitlement. Seller copy records completion and asks check settlement status,never says funds credited/withdrawable or supplies commission/net amount. Seller-wallet settlement notice stays separate pending until source ledger confirmation. Payload type OrderCompleted,audience,kind/order/shop/auction nullable/shipment/orderNo/paid+delivered+completed times/statuses,no other user/contact/money/review URL. Plain text safe rendering and current Commerce/review/settlement permission checks remain client responsibility.

OrderCompletionDispatch event PK,kind int/order/auction nullable/shipment/hash64/Buyer+Seller notification IDs/softdelete;unique(kind,order),nullable unique auction,unique each notification,two restrictive FKs. Event→kind/order→auction locks8025,templates FOR SHARE sortedIDs,atomic receipt/groups/deliveries/log/OrderCompletionAccepted26 audit(old0..25 stable,no raw orderNo/body). Same canonical event replay current state,changed409 ORDER_COMPLETION_CONFLICT,new event same kind/order or auction409 ORDER_COMPLETION_ALREADY_NOTIFIED even hidden. Recreated orders fresh IDs;corrections/reversals separate events,source avoids generic duplication and resolves contradictory domain events.

Append OrderCompleted14(old0..13 stable),two mandatory vi-VN ORDER_COMPLETED_BUYER_PUSH600...033/SELLER...034. Push mandatory,InApp optional unless own mandatory pair,global disable wins. Matrix45 pairs/15mandatory(previous42/14),explicit count fixtures updated. No shared worker/SaveChanges/critical preference policy/Email changes. No/over100 devices skip affected Push only,allowed inbox/sibling retained;Push-only active/all skipped hidden,provider failures/retry isolate recipient/device and keep original plain snapshot.

Migration20261108155627_AddOrderCompletionNotifications adds receipt table/2restrictive FKs/4unique indexes/2templates;Up additive,no previous data mutation,designer/snapshot match. Applied only to isolated test DB. Review target seed ID/code+locale collision,migration and type14/audit26 API/SDK/worker/client readers before source enable. Real Commerce/client/FCM receipt/latency and retention remain pending;Sent only provider accepted,not read/financial proof,crash duplicates possible. No project DB/credentials/live sends/Git index/commit/deploy changes.

Release0 errors/warnings;671 tests passed,0failed/skipped(640 retained+31 new real PG/Kestrel cases with real DeliveryService/fake provider). Covers both kinds/role-specific private payload and financial/review wording,status/time/strict input/auth/size,canonical replay/hidden keys/aliases/concurrent events/recipient/order races/separate kind GUIDs,mandatory/optional/global/device/owner/read,provider failure/retry/revocation/snapshot,atomic rollback/template errors/shared lock/generic collision/corrupted manifest/model/Swagger and prior Delivered history unchanged. Pre-edit DbContext MEDIUM/interface+seed LOW/enums/audit+DI UNKNOWN manually followed,aggregate HIGH(16trackedfiles/23symbols/14processes,index stale/outstanding stages),new files/framework/readers reviewed manually. Scoped diff and new-file whitespace clean;unrelated PasswordChange whitespace/ordinary OrderPayment changes preserved,not edited by this step. Owned temporary PG stopped after final verification. Handoff docs/api/OrderCompletions/ORDER_COMPLETIONS.md. Next backend:confirmed Seller-wallet settlement credit notification,not inferred from Completed.


## Example and handoff

Synthetic historical completion,not payment-to-Seller proof. Source must supply actual Commerce-confirmed outcome/linked recipients/eligible order. Auction requires actual auctionId and kind Auction.
```json
{
  "sourceEventId": "11111111-1111-1111-1111-111111111111",
  "orderId": "22222222-2222-2222-2222-222222222222",
  "shopId": "33333333-3333-3333-3333-333333333333",
  "shipmentId": "44444444-4444-4444-4444-444444444444",
  "buyerUserId": "55555555-5555-5555-5555-555555555555",
  "sellerUserId": "66666666-6666-6666-6666-666666666666",
  "orderKind": "Regular",
  "orderNo": "ORDER-DEMO-001",
  "orderStatus": "Completed",
  "deliveryStatus": "Delivered",
  "paidAt": "2026-11-05T05:00:00Z",
  "deliveredAt": "2026-11-07T05:00:00Z",
  "completedAt": "2026-11-07T05:01:00Z"
}
```

Controller: Notification.Api/Controllers/Public/OrderCompletionsController.cs. Application: Notification.Application/Services/OrderCompletions/. Entity: Notification.Domain/Entities/OrderCompletionDispatch.cs. Persistence: Notification.Infrastructure/Persistence/. Tests: Notification.Tests/OrderCompletionApiIntegrationTests.cs. Request/response/RBAC/errors/enums above define handoff. Client routes by owner notificationId/orderKind and checks current order/review/settlement permission. Source keeps event snapshot immutable,does not generic-dispatch duplicate or enrich replay with financial data.
