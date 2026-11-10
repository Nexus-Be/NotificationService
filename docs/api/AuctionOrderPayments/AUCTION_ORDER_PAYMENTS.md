# Auction order payment notifications
Status: VERIFIED backend — 2026-11-08; source/client/provider integration pending.

## Scope/source boundary
SRS F4-09 Paid order/Settled auction notifies Seller;F4-07 acceptance within24h from Seller notification. Add Buyer confirmation per user payment-flow request. REST only. Source confirms actual winner=Buyer,auction/result/bid/shop/order/recipient linkage,settled payment and authoritative Seller acceptance clock. Notification validates declared shape,not payment truth,does not charge/credit wallet/create orders/accept/ship/refund/cancel/change stock/reputation. Ordinary OrderPayments and pre-payment AuctionWinner/SellerResult remain separate unchanged.

## API/input
POST /api/v1/notifications/auction-order-payments Service/Admin trusted Gateway,16KiB/no-store,200 AuctionOrderPaymentResponse. Two logical items accepted atomically Buyer/Seller. GET /{notificationId} authenticated owner-only,200 InAppNotificationItemResponse,anonymous401/foreign/deleted/non-feature404,no auto-read;Push-only active detail allowed.
Strict required nonempty sourceEventId,auctionId,resultId,winningBidId,orderId,shopId,buyerUserId,sellerUserId;Buyer!=Seller. OrderNo nonblank<=100/no controls. TotalAmount positive decimal18,2,VND;total is source verified order payment,not Seller proceeds. Required declared OrderStatus Paid/AuctionStatus Settled(case-insensitive). Required PaidAt<=now+30sec,SellerAcceptStartedAt>=PaidAt AND<=now+30sec,SellerAcceptDeadline>SellerAcceptStartedAt,normalized UTC microseconds. Source owns24h rule and confirms SellerAcceptStartedAt is the authoritative business notification-availability/acceptance-clock start per F4-07;it must not fabricate provider receipt/reading or use Notification retry time. This field asserts source policy,Notification cannot independently prove notice was available/read. If source cannot supply correct clock,it must resolve its workflow before enabling this producer. No deriving/resetting deadline here. Historical acceptance/deadline accepted as snapshots;client always checks current eligible order/permissions.
TraceId optional<=100/no controls. Unknown JSON rejects QR/bank/account/contact/password/OTP/token/credit/refund/shipping/private docs. No arbitrary payment URL or asks to pay again. Source retries immutable event snapshot,not current mutable deadline/result.

## Persistence/idempotency
AuctionOrderPaidDispatch BaseEntity<Guid>,event PK,order/auction/result/winningBid/BuyerNotification/SellerNotification unique,hash64,softdelete,two restrictive Notification FKs. Event→auction→order→result→bid locks seed8021;both templates shared row locks sortedID;atomic receipt/two groups/all deliveries/logs/AuctionOrderPaymentAccepted22 audit(old0..21 stable,no raw orderNo/price/body). Same event/body replay current states,changed409 AUCTION_ORDER_PAYMENT_CONFLICT. New event reusing order/auction/result/bid409 AUCTION_ORDER_PAYMENT_ALREADY_NOTIFIED including hidden receipts;new recreated auction/outcome uses fresh IDs. One final paid outcome per auction;refund/reversal separate events,never overwrite here. Independent from earlier Winner/Seller result/timeout notices,does not erase history or independently resolve inconsistent source events. Avoid also sending ordinary/generic payment dispatch for same auction order.

## Channels/content/payload
Reuse NotificationType.OrderPaid0 critical,default matrix36/12mandatory unchanged. Two new vi-VN mandatory Push templates AUCTION_ORDER_PAID_BUYER_PUSH600...018 and AUCTION_ORDER_PAID_SELLER_PUSH600...019. Push/InApp mandatory(ignore user opt-out),global disable wins. No Email/common worker/FCM change. One item each,device fanout same parentID,no/over100 devices skips only affected Push;Push-only active/all suppressed hidden,provider failures isolated from sibling item/inbox. Plain snapshots,retry keeps original text/deadline.
Buyer text confirms source-recorded paid amount/time,asks check current order,never asks pay again. Seller text records paid outcome/time and original acceptance deadline,asks check current order/eligibility before action;no automatic shipping/current-pending assertion or net wallet-credit promise. Seller template receives no TotalAmount;missing sensitive placeholder fails entire pair. Payload type OrderPaid,orderKind Auction,audience Buyer/Seller,auction/result/bid/order/shop/orderNo,paidAt,source statuses;Buyer only totalAmount+currency;Seller only sellerAcceptStartedAt/sellerAcceptDeadline,no Buyer identity/contact/amount. Client renders plain text and checks actual Commerce/Auction state/ownership before actions. Sent=provider accepted,not Seller read/deadline start/actual financial verification.

## Responses/errors/enums
Response sourceEventId,orderId,isReplay,buyer/seller each notificationId,userId,notificationStatus,inboxVisible,deliveries[id,channel,status,errorCode]. Pending=durable queue,InApp Sent=persisted.
400 VAL_INVALID_INPUT/TEMPLATE_DATA_MISSING;401 UNAUTHORIZED;403 FORBIDDEN POST;404 TEMPLATE_NOT_FOUND/NOTIFICATION_NOT_FOUND;409 AUCTION_ORDER_PAYMENT_CONFLICT/AUCTION_ORDER_PAYMENT_ALREADY_NOTIFIED/AUCTION_ORDER_PAYMENT_TEMPLATE_INVALID/CHANNEL_NOT_FOUND/TEMPLATE_INVALID/DELIVERY_STATE_INVALID;413 REQUEST_BODY_TOO_LARGE;persistence500 rollback. Skip CHANNEL_DISABLED/PUSH_NO_ACTIVE_DEVICES/PUSH_DEVICE_LIMIT.
NotificationType.OrderPaid0,AuditAction.AuctionOrderPaymentAccepted22,Email0/Push1/InApp2,ScopeAll0;DeliveryStatus Pending0/Sending1/Sent2/Failed3/Skipped4;NotificationStatus Pending0/Completed1/Partial2/Failed3;PascalCase API.

## Plan/context/impact/verification
Read AGENTS/rules/specs and new-feature-endpoint/service-api-doc/controller-conventions workflow,SRS F4-07/09,ordinary OrderPayments and auction lifecycle features/config/DTO/tests. GitNexus pre-edit DbContext MEDIUM/interface+seed LOW,audit+DI UNKNOWN manually followed;index stale one commit. Manual significant financial-wording/recipient/acceptance-clock risk disclosed. No shared worker/type/preference policy changes.
- [x] Contract and plan before C#.
- [x] Receipt/config/audit22/DbSets/DI,DTO/service/controller/2templates.
- [x] Additive migration/designer/snapshot:table/2FKs/6unique indexes/2seeds.
- [x] PG/Kestrel tests states/clock/strict input/auth/size,Buyer/Seller payload/privacy/grouping,mandatory/global/device/owner/read/retry,replay/aliases/races/atomic rollback/template locks/model/Swagger/history.
- [x] Full Release0errors/warnings/full519 regression,Git diff/GitNexus,docs/handoff/checklists,owned PG cleanup.

Rollout migration/check seed ID/code+locale collision;all readers support audit22/type0/auction orderKind/owner route. Source must have authoritative acceptance-clock and avoid ordinary/generic duplicate dispatch. Source/client/FCM/receipt/latency/retention/at-least-once crash duplicates remain pending. No project DB/credentials/live sends/Git index/commit/deploy. Next backend:Seller accepted/rejected/order-cancel outcomes,never treat payment notice as fulfillment.


## Auction order payment — 2026-11-08

Implemented Notification-side POST /api/v1/notifications/auction-order-payments(Service/Admin,16KiB/no-store,200 AuctionOrderPaymentResponse) and GET /{notificationId}(authenticated owner,no auto-read,anonymous401/foreign/deleted/non-feature404). SRS F4-09 paid/settled Seller notice plus Buyer confirmation;distinct from initial winner result and ordinary orders. Strict required auction/result/bid/order/shop/Buyer/Seller IDs,distinct users,positive decimal18,2 VND order total,orderNo,PaidAt,declared Order Paid/Auction Settled,SellerAcceptStartedAt>=PaidAt and<=now+30s,SellerAcceptDeadline>startedAt;UTC microseconds. Unknown contacts/QR/bank/secrets/credit/refund/shipping flags rejected. Source proves actual payment/winner/ownership/linkage and provides authoritative acceptance-clock start per F4-07(Seller notification availability) and24h deadline. Notification cannot prove source clock or actual receipt/read;provider accepted is not read/start of acceptance clock. Source must resolve correct clock before enabling caller;never derive/reset deadline from Notification retry/provider callback. Historical outcomes retain original times and require checking current target state/permissions before action. No payment/order/wallet/stock/fulfillment/reputation mutation.

AuctionOrderPaidDispatch event PK,unique order/auction/result/bid/BuyerNotification/SellerNotification,two restrictive FKs,hash64/softdelete. Event→auction→order→result→bid advisory locks8021,both template shared row locks sortedID. Receipt/two logical groups/deliveries/log/AuctionOrderPaymentAccepted22 audit atomic(old0..21 stable,no raw orderNo/price/body). Same canonical event replay current branch states,changed409 AUCTION_ORDER_PAYMENT_CONFLICT;new event reusing any outcome identity409 AUCTION_ORDER_PAYMENT_ALREADY_NOTIFIED even hidden. Recreated outcome fresh IDs,refund/reversal separate future events. Earlier Winner/Seller result history preserved. Source must avoid also ordinary/generic dispatch for same auction-order payment;independent endpoint receipts do not reconcile wrong cross-flow calls/source contradictions.

Reuse OrderPaid0 critical;matrix36/12mandatory unchanged. Two mandatory vi-VN templates AUCTION_ORDER_PAID_BUYER_PUSH600...018 and AUCTION_ORDER_PAID_SELLER_PUSH600...019. One logical item each with InApp/Push devices,mandatory overrides user opt-out,global disable wins. No Email/common worker/FCM changes. No/over100 devices skips only affected Push,allowed inbox/sibling remain;Push-only active/all skipped hidden,provider failures/retry isolated. Immutable plain parent snapshots. Buyer confirms source-recorded amount/time without asking pay again;Seller sees recorded paid time/original acceptance deadline and current-state instruction,no proceeds/wallet-credit/automatic shipping claim. Payload type OrderPaid,orderKind Auction,audience,auction/result/bid/order/shop/orderNo/paidAt/statuses;Buyer only total/currency,Seller only acceptance-clock fields,neither other user identity/contact. No Seller TotalAmount template placeholder accepted.

Migration20261108144251_AddAuctionOrderPaymentNotifications adds receipt table/2FKs/6unique indexes/2templates;Up additive,no prior data changes,designer/snapshot synchronized. Only isolated test DB migrated. Target migration/seed ID+code-locale collision/audit22+auction orderKind-aware clients/SDK/worker and correct source clock required before rollout. Real source/client/FCM/receipt/latency/retention remain pending;Sent only provider acceptance,provider crash duplicates possible. No project DB/credentials/live sends/Git index/commit/deploy changes.

Release0 errors/warnings;551 tests passed,0failed/skipped(519 retained+32 new real PG/Kestrel cases with real DeliveryService/fake provider). Covers both grouped branches and role-specific privacy/source clock/states/auth/strict input/size,mandatory/global/device/owner/read/suppression,failures/retry/token revocation,canonical replay/hidden keys/aliases/concurrent recipients/orders,atomic rollback/template configuration/render/shared locks/generic collision/corrupted manifest/model/Swagger,late deadlines and preserved initial Winner/Seller history. Existing ordinary test suite unchanged;new fixture adapted for source clock and auction templates. GitNexus pre-edit DbContext MEDIUM/interface+seed LOW/audit+DI UNKNOWN manually followed;tracked aggregate MEDIUM(15files/22symbols,index one commit behind),new files/framework reviewed manually. Disposable owned PG stopped after final verification. Handoff docs/api/AuctionOrderPayments/AUCTION_ORDER_PAYMENTS.md. Next backend:Seller accepted/rejected/order cancellation outcomes;source integration still pending.


## Example and handoff

Synthetic historical request,not proof of payment or Seller receipt. Source must supply actual linked identities and authoritative acceptance-clock policy.
```json
{
  "sourceEventId": "11111111-1111-1111-1111-111111111111",
  "auctionId": "22222222-2222-2222-2222-222222222222",
  "resultId": "33333333-3333-3333-3333-333333333333",
  "winningBidId": "44444444-4444-4444-4444-444444444444",
  "orderId": "55555555-5555-5555-5555-555555555555",
  "shopId": "66666666-6666-6666-6666-666666666666",
  "buyerUserId": "77777777-7777-7777-7777-777777777777",
  "sellerUserId": "88888888-8888-8888-8888-888888888888",
  "orderNo": "AUCTION-DEMO-001",
  "totalAmount": 150000,
  "currency": "VND",
  "orderStatus": "Paid",
  "auctionStatus": "Settled",
  "paidAt": "2026-11-07T05:00:00Z",
  "sellerAcceptStartedAt": "2026-11-07T05:01:00Z",
  "sellerAcceptDeadline": "2026-11-08T05:01:00Z"
}
```

Controller: Notification.Api/Controllers/Public/AuctionOrderPaymentsController.cs. Application: Notification.Application/Services/AuctionOrderPayments/. Entity: Notification.Domain/Entities/AuctionOrderPaidDispatch.cs. Persistence: Notification.Infrastructure/Persistence/. Tests: Notification.Tests/AuctionOrderPaymentApiIntegrationTests.cs. Request/response/RBAC/errors/enums above define handoff. Client routes by orderKind Auction and owner notificationId,checks current authorized auction-order detail. Do not also call ordinary order-payments or generic templates for same auction payment.
