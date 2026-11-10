# Completed order refund notification
Status: VERIFIED backend — 2026-11-08; source/client/provider integration pending.

## Scope/source boundary
SRS F8-01 refunds credit Buyer wallet,not bank;source Commerce/Wallet owns eligibility before carrier handover,refund/payment/ledger linkage and any inventory/reputation side effects. Accept confirmed completed refund operations for Regular/Auction orders,Buyer only. REST only. Notification does not request/refund money,write ledger/credit balances,check fulfillment eligibility,release stock or apply points. Cancellation/decision alone is not refund proof. Source calls only after refund and linked wallet credit commit;Notification validates declared fields,not actual source truth/recipient ownership. Failure/pending/requested notifications not in this endpoint. Seller notice remains separate if later required.

## API/strict input
POST /api/v1/notifications/order-refunds Service/Admin trusted Gateway,16KiB/no-store,200 OrderRefundResponse. GET /{notificationId} authenticated Buyer owner only,200 InAppNotificationItemResponse,no auto-read,anonymous401/foreign/deleted/non-feature404,Push-only active detail allowed.
Required nonempty sourceEventId,refundId,walletCreditTransactionId,orderId,buyerUserId. OrderKind Regular/Auction case-insensitive,Auction requires nonempty auctionId,Regular must omit/null it. OrderNo nonblank<=100/no controls;RefundAmount positive decimal18,2,VND. Required RefundStatus Completed,WalletCreditStatus Completed,RefundDestination BuyerWallet(case-insensitive). Required RequestedAt<=WalletCreditedAt<=CompletedAt<=now+30sec,normalized UTC microseconds. Historical events allowed,never reinterpret timestamp as current balance or add delay to completion. TraceId optional<=100/no controls. Unknown JSON rejects bank/card/address/contact/current balance/payment URL/points/stock flags/password/OTP/token/private docs. No Seller identity or freeform reason input. Source verifies actual credited amount/account/transaction matches refund/order/Buyer and no over-refund;Notification cannot prove wallet balance/eligibility.

## Idempotency/persistence
OrderRefundDispatch BaseEntity<Guid>,event PK,unique RefundId/WalletCreditTransactionId/NotificationId,hash64,OrderKind int,OrderId,AuctionId nullable,softdelete,restrictive Notification FK;nonunique(kind,order) index for history. Event→refund→wallet-credit advisory locks8023,template FOR SHARE;receipt/group/deliveries/log/OrderRefundAccepted24 audit atomic(old0..23 stable,no amount/orderNo/raw request).
Canonical normalized IDs/kind/orderNo/amount fixed2digits/VND/statuses/destination/UTC times,trace excluded. Same event/body replay current deliveries,changed409 ORDER_REFUND_CONFLICT;new event alias same refund or wallet credit409 ORDER_REFUND_ALREADY_NOTIFIED even hidden. Same source operation cannot change Buyer or amount through retry. Multiple distinct confirmed refund+credit operations may refer to same order;source owns correctness/aggregate limits,the notice does not assert whole order refunded or remaining amount. Refund reversals/corrections separate future events,never overwrite. Independent of earlier paid/decision receipt and does not erase history or prove cancellation;do not also generic-dispatch same operation.

## Channels/content/payload
Append NotificationType.OrderRefunded13(old0..12 stable),AuditAction.OrderRefundAccepted24. One new vi-VN mandatory Push ORDER_REFUND_COMPLETED_PUSH ID60000000-0000-0000-0000-000000000020. One Buyer logical item,Push mandatory/InApp optional unless own mandatory pair,global disable wins. Matrix42 pairs/14mandatory(previous39/13),update explicit count fixtures;no common worker/critical policy changes or Email. No/over100 devices skip Push only,permitted inbox retained;Push-only active/all skipped hidden. Worker/device ownership/retry snapshots unchanged.
Plain content records order,confirmed refund amount into Buyer wallet at source CompletedAt,asks check current transaction/balance in Nexus. No bank transfer/withdrawal/full-order-refund/current balance/stock/reputation promise. Payload type OrderRefunded,audience Buyer,orderKind/orderId/auctionId nullable/orderNo/refundId/walletCreditTransactionId/refundAmount/VND/source timestamps/statuses/destination;no other user/contact/wallet account/balance. IDs route only to authorized target APIs;client safely renders text and checks current ownership/state. Sent means provider accepted,not read/independent financial proof.

## Response/errors/enums
Response sourceEventId,refundId,orderId,notificationId,userId,notificationStatus,inboxVisible,isReplay,deliveries[id,channel,status,errorCode]. Pending=durable queue,InApp Sent=persisted.
400 VAL_INVALID_INPUT/TEMPLATE_DATA_MISSING;401 UNAUTHORIZED;403 FORBIDDEN POST;404 TEMPLATE_NOT_FOUND/NOTIFICATION_NOT_FOUND;409 ORDER_REFUND_CONFLICT/ORDER_REFUND_ALREADY_NOTIFIED/ORDER_REFUND_TEMPLATE_INVALID/CHANNEL_NOT_FOUND/TEMPLATE_INVALID/DELIVERY_STATE_INVALID;413 REQUEST_BODY_TOO_LARGE;persistence500 rollback. Skips CHANNEL_DISABLED/PREFERENCE_DISABLED/PUSH_NO_ACTIVE_DEVICES/PUSH_DEVICE_LIMIT.
NotificationType.OrderRefunded13,NotificationOrderKind Regular0/Auction1,AuditAction.OrderRefundAccepted24;Email0/Push1/InApp2,ScopeAll0;DeliveryStatus Pending0/Sending1/Sent2/Failed3/Skipped4;NotificationStatus Pending0/Completed1/Partial2/Failed3;API PascalCase.

## Plan/context/impact/verification
Follow AGENTS/rules/specs,new-feature-endpoint/service-api-doc/controller-conventions workflow. Read SRS F8-01,Commerce refund events,OrderDecision and single-recipient notification code/config/DTO/tests. Pre-edit DbContext MEDIUM/interface+seed LOW/enums+DI UNKNOWN manually follow framework/reader paths,index stale. Previous aggregate HIGH includes outstanding DbContext/enum stages;do not treat UNKNOWN/HIGH as low,review callers/full regression and require reader upgrades before rollout. Significant source financial/recipient/history wording risk disclosed. No shared worker/SaveChanges/critical policy changes.
- [x] Contract/plan before C#.
- [x] Receipt/config/type13/audit24/DbSets/DI,DTO/service/controller/1template.
- [x] Additive migration/designer/snapshot:table/FK/3unique indexes+order history index/1seed.
- [x] PG/Kestrel tests both kinds/confirmed status/destination/amount/time/auth/strict input/size,Buyer-only privacy/grouping,policy/device/owner/read/retry,replay/aliases/races/rollback/template lock/model/Swagger and preserved payment/cancellation history.
- [x] Full Release0errors/warnings/full583 regression,Git diff/GitNexus,docs/handoff/checklists,owned PG cleanup.

Rollout reviewed migration/seed ID+code-locale collisions and type13/audit24 readers(API/SDK/worker) before source enable. Source/client/FCM/receipt/latency/retention and provider crash duplication remain pending. No project DB/credentials/live sends/Git index/commit/deploy. Next backend:shipment-created/status notifications from confirmed fulfillment outcomes;other cancellation reasons/order completion remain pending.


## Confirmed Buyer-wallet refunds — 2026-11-08

Implemented POST /api/v1/notifications/order-refunds(Service/Admin,16KiB/no-store,200 OrderRefundResponse),GET /{notificationId}(authenticated owner,no auto-read,anonymous401/foreign/deleted/non-feature404). SRS F8-01 Buyer-wallet refund,Regular/Auction,Buyer only. Strict event/refund/wallet-credit/order/Buyer IDs,conditional AuctionId,orderNo,positive decimal18,2 VND amount;required RefundStatus Completed/WalletCreditStatus Completed/RefundDestination BuyerWallet;requested<=credited<=completed<=now+30s normalized UTC microseconds. Rejects pending/failed/requested/bank destinations,invalid money/time/kind and unknown contacts/bank/balance/stock/points/secrets. Source confirms actual credit committed and linked to refund/order/Buyer,eligibility/aggregate over-refund policy;Notification validates declared shape,not source financial truth,does not credit/write ledger/refund/release stock/apply points. Cancellation alone is not completion proof. Historical notice records source time/this operation amount,never current balance/full-order refund/bank transfer/withdrawal or stock/reputation claim. Seller/failure/pending notices remain outside endpoint.

OrderRefundDispatch event PK,unique refund/wallet-credit/notification IDs,kind/order history nonunique index,nullable auction,hash64/softdelete/restrictive FK. Event→refund→credit locks8023,template shared lock,atomic receipt/group/deliveries/log/OrderRefundAccepted24 audit(old0..23 stable,no amount/orderNo/raw request). Same normalized event replay current state,changed409 ORDER_REFUND_CONFLICT;new event reusing refund or credit409 ORDER_REFUND_ALREADY_NOTIFIED including hidden keys. Changed Buyer/amount never overwrites accepted operation. Distinct confirmed refund+credit operations may reference same order;source owns aggregate limits and correctness,not permission for duplicate or extra refund. Independent from paid/decision receipt,history retained;reversals/corrections separate future workflow. Avoid generic dispatch duplicate.

Append OrderRefunded13(old0..12 stable),one vi-VN mandatory Push ORDER_REFUND_COMPLETED_PUSH600...020. One Buyer logical item,mandatory Push/optional InApp unless own mandatory pair,global disabled wins. Matrix42 pairs/14mandatory(previous39/13),explicit count fixtures updated;common worker/SaveChanges/critical policy/Email unchanged. No/over100 devices skip Push only,permitted inbox preserved;Push-only active/all suppressed hidden. Ownership/provider errors/retry keep original plain snapshot. Payload type OrderRefunded,audience Buyer,kind/order/auction/orderNo/refund/credit/amount/VND/source times/statuses/destination,no other identity/account/contact/balance. Client safely renders text,checks current target ownership/state and balance through authorized source API. Sent is provider accepted,not read/financial verification.

Migration20261108151440_AddOrderRefundNotifications adds receipt table/restrictive FK/3unique indexes+order-history index/one template,integer kind;Up additive,no existing data changes,designer/snapshot match. Only isolated test DB migrated. Review seed ID/code+locale collision,target migration and type13/audit24 readers(API/SDK/worker) before source enable. Real source/client/FCM receipt/latency,retention and at-least-once crash duplicates remain pending. No project DB/credentials/live sends/Git index/commit/deploy changes.

Release0 errors/warnings;607 tests passed,0failed/skipped(583 retained+24 new real PG/Kestrel cases with real DeliveryService/fake provider). Covers both order kinds/Buyer-only privacy/content,required confirmation/destination/money/times/auth/strict input/size,historical/equality bounds,canonical replay/hidden keys/recipient and refund+credit alias races/distinct operations,mandatory/global/optional/device/owner/read-badge,worker permanent/transient errors and immutable retry/revocation,atomic rollback/template errors/shared lock/generic collision/corrupted manifest/model/Swagger and paid/cancelled history retained. Pre-edit DbContext MEDIUM/interface+seed LOW/enums+DI UNKNOWN manually followed. Final GitNexus tracked aggregate HIGH(15files/23symbols/14processes),includes outstanding stages/DbContext/model/enum paths,index stale one commit,new files/framework reviewed manually. Shared SaveChanges/worker untouched;HIGH reader rollout risk remains explicit,not downgraded. Owned disposable PG stopped after final verification. Handoff docs/api/OrderRefunds/ORDER_REFUNDS.md. Next backend:confirmed shipment creation/status notifications;other cancellations/order completion remain pending.


## Example and handoff

Synthetic historical completed refund;source must prove actual committed credit,amount and recipient linkage,not invent facts to match this example. Auction requires actual auctionId and kind Auction.
```json
{
  "sourceEventId": "11111111-1111-1111-1111-111111111111",
  "refundId": "22222222-2222-2222-2222-222222222222",
  "walletCreditTransactionId": "33333333-3333-3333-3333-333333333333",
  "orderId": "44444444-4444-4444-4444-444444444444",
  "buyerUserId": "55555555-5555-5555-5555-555555555555",
  "orderKind": "Regular",
  "orderNo": "ORDER-DEMO-001",
  "refundAmount": 125000,
  "currency": "VND",
  "refundStatus": "Completed",
  "walletCreditStatus": "Completed",
  "refundDestination": "BuyerWallet",
  "requestedAt": "2026-11-07T05:00:00Z",
  "walletCreditedAt": "2026-11-07T05:01:00Z",
  "completedAt": "2026-11-07T05:01:01Z"
}
```

Controller: Notification.Api/Controllers/Public/OrderRefundsController.cs. Application: Notification.Application/Services/OrderRefunds/. Entity: Notification.Domain/Entities/OrderRefundDispatch.cs. Persistence: Notification.Infrastructure/Persistence/. Tests: Notification.Tests/OrderRefundApiIntegrationTests.cs. Request/response/RBAC/errors/enums above define handoff. Source keeps IDs/money/times immutable across durable REST retries. Client uses owner notificationId and authorized refund/wallet/order APIs for current state,not arbitrary URL.
