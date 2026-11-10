# Confirmed Seller-wallet order settlement notice
Status: VERIFIED backend — 2026-11-08; Commerce/Wallet/client/provider integration pending.

## Scope/source boundary
SRS F1-11/F4-11:Seller receives proceeds after order completion;ordinary wallet credit can fail while order Completed and auction transfer timing is not fixed. Accept only source-confirmed final settlement and Seller-wallet credit,not mere completion/request/pending. REST only. Source Commerce/Wallet verifies order/Seller/auction linkage,completion/eligibility/hold/dispute/ledger facts and actual final net credit. Notification validates declared shape,does not settle/credit/write ledger/compute commission/refund/release funds/change reputation or initiate bank withdrawal. Seller only;Buyer finance details not accepted. Net amount is exact source wallet credit,not buyer total/gross proceeds or current available balance. Adjustment/reversal/installment workflows separate future events,not overwrites.

## API/input
POST /api/v1/notifications/order-settlements Service/Admin trusted Gateway,16KiB/no-store,200 OrderSettlementResponse. GET /{notificationId} authenticated owner-only,200 InAppNotificationItemResponse,no auto-read,anonymous401/foreign/deleted/non-feature404,Push-only active detail allowed.
Required nonempty sourceEventId,settlementId,walletCreditTransactionId,orderId,sellerUserId. OrderKind Regular/Auction case-insensitive;Auction requires nonempty auctionId,Regular omit/null. OrderNo nonblank<=100/no controls;NetAmount positive decimal18,2,VND from source ledger,not recalculated here. Required OrderStatus Completed,SettlementStatus Completed,WalletCreditStatus Completed,SettlementDestination SellerWallet(case-insensitive). Required OrderCompletedAt<=WalletCreditedAt<=SettledAt<=now+30s,UTC microseconds;historical notices preserve source times. TraceId optional<=100/no controls. Strict unknown JSON rejects buyer identity/contact/gross/fee/rate/commission/balance/withdrawable/bank/card/payment URL/refund/stock/points flags/password/OTP/token/private docs. Source enforces actual net amount/account/currency/transaction linkage and financial policy;Notification cannot independently verify credit/available balance. No prior completion-notification receipt required for actual confirmed source outcome.

## Persistence/idempotency
OrderSettlementDispatch BaseEntity<Guid>,event PK,SettlementId,WalletCreditTransactionId,OrderKind Regular0/Auction1 int,OrderId,AuctionId nullable,NotificationId,hash64,softdelete/restrictive Notification FK. Unique settlement,credit,(kind,order),nullable auction,notification (5indexes). Event→settlement→wallet-credit→kind/order→auction(if present) locks8026;template FOR SHARE. Atomic receipt/group/delivery/log/OrderSettlementAccepted27 audit(old0..26 stable,no amount/orderNo/raw request).
Canonical normalized IDs/kind/orderNo/net fixed2digits/VND/statuses/destination/UTC times,trace excluded. Same event/body replay current state;changed409 ORDER_SETTLEMENT_CONFLICT. New event reusing settlement/credit/kind-order/auction409 ORDER_SETTLEMENT_ALREADY_NOTIFIED even hidden. One final net settlement notice per source order,not installments/adjustments;fresh recreated order/auction/settlement/credit uses fresh IDs. Changed Seller/amount never overwrites accepted operation. Separate completion/payment/refund history preserved;source resolves contradictory events and avoids generic duplicate dispatch.

## Channels/content/payload
Append NotificationType.OrderSellerSettled15(old0..14 stable),AuditAction.OrderSettlementAccepted27. One vi-VN mandatory Push ORDER_SELLER_SETTLED_PUSH600...035. Seller one logical item,mandatory Push/optional InApp unless own mandatory pair,global disable wins. Matrix48 pairs/16mandatory(previous45/15),explicit fixtures updated. No Email/common worker/SaveChanges/critical preference policy change. Missing/over100 devices skips only Push,allowed inbox retained;Push-only active/all skipped hidden. Device ownership/provider errors/retry preserve plain original snapshot.
Body records final amount credited to Seller wallet at source SettledAt,asks check current transaction/balance in Nexus;no funds withdrawable/bank transfer/gross/commission calculation/current balance/reputation claim. Payload type OrderSellerSettled,audience Seller,kind/order/auction nullable/orderNo/settlementId/walletCreditTransactionId/netAmount/VND/source times/statuses/destination;no Buyer identity/account/contact/bank/balance/fees. Client safely renders text and checks authorized source transaction/order/current wallet state. Sent=provider accepted,not read/independent financial proof.

## Response/errors/enums
Response sourceEventId,settlementId,orderId,notificationId,userId,notificationStatus,inboxVisible,isReplay,deliveries[id,channel,status,errorCode]. Pending durable queue,InApp Sent persisted.
400 VAL_INVALID_INPUT/TEMPLATE_DATA_MISSING;401 UNAUTHORIZED;403 FORBIDDEN POST;404 TEMPLATE_NOT_FOUND/NOTIFICATION_NOT_FOUND;409 ORDER_SETTLEMENT_CONFLICT/ORDER_SETTLEMENT_ALREADY_NOTIFIED/ORDER_SETTLEMENT_TEMPLATE_INVALID/CHANNEL_NOT_FOUND/TEMPLATE_INVALID/DELIVERY_STATE_INVALID;413 REQUEST_BODY_TOO_LARGE;persistence500 rollback. Skips CHANNEL_DISABLED/PREFERENCE_DISABLED/PUSH_NO_ACTIVE_DEVICES/PUSH_DEVICE_LIMIT.
NotificationType.OrderSellerSettled15,NotificationOrderKind Regular0/Auction1,AuditAction.OrderSettlementAccepted27;Email0/Push1/InApp2,ScopeAll0;DeliveryStatus Pending0/Sending1/Sent2/Failed3/Skipped4;NotificationStatus Pending0/Completed1/Partial2/Failed3;API PascalCase.

## Plan/context/impact/verification
Follow AGENTS/rules/specs,new-feature-endpoint/service-api-doc/controller-conventions workflow. Read SRS F1-11/F4-11/F8,refund and completion features/controllers/DTOs/entities/config/tests. Pre-edit DbContext MEDIUM/interface+seed LOW/enums/audit+DI UNKNOWN manually follow readers/framework;prior aggregate HIGH(index stale/outstanding stages) not treated low. Significant net amount/recipient/credit timing/bank wording risks disclosed. Existing external formatting/whitespace changes preserved.
- [x] Contract/plan before C#.
- [x] Receipt/config/type15/audit27/DbSets/DI,DTO/service/controller/1template.
- [x] Additive migration/designer/snapshot:table/FK/5unique indexes/1seed.
- [x] PG/Kestrel tests both kinds,confirmed order/settlement/credit/destination/money/time/auth/strict input/size/privacy/grouping,policy/device/owner/read/retry,replay/aliases/races/rollback/template locks/model/Swagger and completion history retained.
- [x] Full Release0errors/warnings/full671 regression,scoped diff/GitNexus,docs/handoff/checklists,owned PG cleanup.

Rollout migration/seed ID+code-locale collision and type15/audit27 API/SDK/worker/client readers before source enable. Real Commerce/Wallet/client/FCM receipt/latency,retention and provider crash duplicates remain pending. No project DB/credentials/live sends/Git index/commit/deploy. Next backend:ordinary unpaid payment-timeout cancellation notice;other cancellation reasons/integration remain pending.


## Confirmed Seller-wallet settlement — 2026-11-08

Implemented POST /api/v1/notifications/order-settlements(Service/Admin,16KiB/no-store,200 OrderSettlementResponse),GET /{notificationId}(authenticated owner,no auto-read,anonymous401/foreign/deleted/non-feature404). Regular/Auction,Seller only,after source confirms actual Completed order/final Completed settlement/Completed wallet credit and SellerWallet destination. Strict IDs/kind/conditional auction/orderNo,positive decimal18,2 VND final net amount,orderCompleted<=credited<=settled<=now+30s normalized UTC microseconds. Rejects pending/failed/requested/wrong order status/bank/Buyer-wallet destination,bad money/times and unknown Buyer/gross/fee/commission/balance/withdrawable/contact/bank/secrets fields. Source verifies final credited amount/account/transaction/order linkage and eligibility;Notification validates declared shape,not ledger truth,does not compute commission/credit/pay/refund/withdraw/write ledger or invent settlement schedule. Historical notices preserve source times,not current balance/withdrawable funds or bank-transfer proof. No prior completion notice required for real confirmed source outcome;settlement notification remains distinct from Completed.

OrderSettlementDispatch event PK,unique settlement/credit/(kind,order)/nullable auction/notification,kind int/hash64/softdelete/restrictive FK. Event→settlement→credit→kind/order→auction locks8026,template shared lock,atomic receipt/group/deliveries/log/OrderSettlementAccepted27 audit(old0..26 stable,no amount/orderNo/raw input). Same canonical event replay current state,changed409 ORDER_SETTLEMENT_CONFLICT,new event alias reusing any settlement/credit/kind-order/auction409 ORDER_SETTLEMENT_ALREADY_NOTIFIED including hidden keys. One final net settlement per order,not installment/adjustment/reversal workflow;fresh recreated source orders/auctions/operations use fresh IDs. Separate Regular/Auction tables may share order GUID;source owns financial correctness. Changed Seller/amount never overwrites accepted event;source must avoid generic duplicate dispatch. Earlier completion history preserved and Buyer cannot view Seller-credit detail.

Append OrderSellerSettled15(old0..14 stable),mandatory vi-VN ORDER_SELLER_SETTLED_PUSH600...035. One Seller item,Push mandatory/InApp optional unless own mandatory pair,global disable wins. Matrix48 pairs/16mandatory(previous45/15),explicit count fixtures updated;no Email/common worker/SaveChanges/critical policy changes. Device absence/over100 skips only Push,allowed inbox retained;Push-only active/all skipped hidden. Ownership/failure isolation/retry keep original plain snapshot. Body records exact source net credit/Seller wallet/time,asks check current transaction/balance,no commission/gross/current balance/bank/withdrawal claim. Payload type OrderSellerSettled,audience Seller,kind/order/auction/orderNo/settlement/credit/net/VND/source times/statuses/destination,no Buyer/contact/account/fee/balance. Client safely renders text and checks authorized order/transaction/current wallet state. Sent only provider accepted,not read/independent financial verification.

Migration20261108161249_AddOrderSettlementNotifications adds receipt table/restrictive FK/5unique indexes/one template;Up additive,no prior data mutation,designer/snapshot match. Only isolated test DB migrated. Review target seed-ID/code+locale collision,migration and type15/audit27 API/SDK/worker/client readers before source enable. Commerce/Wallet/client/FCM receipt/latency,retention and provider crash duplicates remain pending. No project DB/credentials/live sends/Git index/commit/deploy actions by this step. Git index changed externally during work;existing staged changes were preserved,not restaged/reset. Review working-tree snapshot/migration together for later commit.

Release0 errors/warnings;695 tests passed,0failed/skipped(671 retained+24 new real PG/Kestrel cases with real DeliveryService/fake provider). Covers both kinds/Seller-only amount/privacy,confirmed order/settlement/credit/destination/money/time/auth/strict input/size,canonical replay/hidden keys/settlement-credit-order-auction aliases and races/one final order rule/separate kinds,mandatory/optional/global/device/owner/read,worker permanent/transient failure/snapshot retry/revocation,atomic rollback/template errors/shared lock/generic collision/corrupted manifest/model/Swagger and prior completion history. Pre-edit DbContext MEDIUM/interface+seed LOW/enums/audit+DI UNKNOWN manually followed,aggregate HIGH(138files/23symbols/14processes at review,includes externally staged prior stages),new files/framework/readers manually reviewed. Scoped whitespace/diff checks clean;unrelated source/formatting changes preserved. Owned temporary PG stopped after final verification. Handoff docs/api/OrderSettlements/ORDER_SETTLEMENTS.md. Next backend:ordinary unpaid payment-timeout cancellation;other cancellations/integration remain pending.


## Example and handoff

Synthetic final credit,not evidence of actual payout or bank withdrawal. Source supplies actual final net ledger credit/linked recipient/eligible Completed order. Auction requires actual auctionId and kind Auction. No commission formula is defined by Notification.
```json
{
  "sourceEventId": "11111111-1111-1111-1111-111111111111",
  "settlementId": "22222222-2222-2222-2222-222222222222",
  "walletCreditTransactionId": "33333333-3333-3333-3333-333333333333",
  "orderId": "44444444-4444-4444-4444-444444444444",
  "sellerUserId": "55555555-5555-5555-5555-555555555555",
  "orderKind": "Regular",
  "orderNo": "ORDER-DEMO-001",
  "netAmount": 125000,
  "currency": "VND",
  "orderStatus": "Completed",
  "settlementStatus": "Completed",
  "walletCreditStatus": "Completed",
  "settlementDestination": "SellerWallet",
  "orderCompletedAt": "2026-11-07T05:00:00Z",
  "walletCreditedAt": "2026-11-07T07:00:00Z",
  "settledAt": "2026-11-07T07:00:01Z"
}
```

Controller: Notification.Api/Controllers/Public/OrderSettlementsController.cs. Application: Notification.Application/Services/OrderSettlements/. Entity: Notification.Domain/Entities/OrderSettlementDispatch.cs. Persistence: Notification.Infrastructure/Persistence/. Tests: Notification.Tests/OrderSettlementApiIntegrationTests.cs. Request/response/RBAC/errors/enums above define handoff. Source keeps IDs/money/times immutable across durable REST retry;client routes owner notificationId to current authorized wallet transaction/order details. Do not generic-dispatch duplicate or enrich completion replay with credit data.

## Handoff finalized — 2026-11-09
Resumed the unfinished documentation/cleanup after the previous tool approval review hit a usage limit. The previous full Release test run passed 695 tests,0failed/skipped. Rechecked Release build today:0errors/0warnings;scoped staged and unstaged diff checks passed. Finalized contract/checklists and stopped the owned settlement PostgreSQL test container. No project DB migration,credentials/live sends,Git staging/reset/commit or deployment performed. The existing Git index was preserved. Next step remains ordinary unpaid payment-timeout cancellation.