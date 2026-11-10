# Seller auction payment timeout notification
Status: VERIFIED backend — 2026-11-08; source/client/provider integration pending.

## Objective/source boundary
SRS F4-09 says Winner unpaid after24h leads to order cancellation/auction failure and Seller receives timeout notice without Seller penalty. Implement Notification-side Seller notice,independent from Winner timeout and initial Seller result. REST only. Source owns actual Seller/Winner/auction/order linkage,unpaid/cancel race,committed Order Cancelled/Auction Failed and reliable immutable retry. Notification only validates declared shape;does not cancel orders,release stock,apply points/fines,create auction,verify payment or source permissions. Never call from timer before confirmed final states.

## API/input
POST /api/v1/notifications/auction-seller-payment-timeouts Service/Admin trusted Gateway,16KiB,no-store,200 pure AuctionSellerPaymentTimeoutResponse. GET /{notificationId} authenticated owner only,200 InAppNotificationItemResponse;anonymous401/foreign/deleted/non-feature404,Push-only active detail allowed,GET no read mutation.
Required nonempty sourceEventId,auctionId,resultId,winningBidId,orderId,winnerUserId,sellerUserId;Winner differs from Seller. AuctionTitle nonblank<=200;TraceId optional<=100,no control chars. Exact case-insensitive OrderStatus Cancelled/AuctionStatus Failed. Required SettledAt<=serverNow+30s,PaymentDeadline>SettledAt AND<=serverNow,TimedOutAt>=PaymentDeadline AND<=serverNow+30s;normalize UTC microseconds. Historical notices retain source times,never reset deadline. Source enforces original24h policy. Strict unknown JSON:no winner/seller email/name/contact,price/currency,points/fines/payment URLs/password/OTP/token/stock or refund flags.

Canonical normalized IDs/title/statuses/times,trace excluded. Event PK same snapshot replay current deliveries;changed409 AUCTION_SELLER_PAYMENT_TIMEOUT_CONFLICT. New event alias reusing auction/result/bid/order409 AUCTION_SELLER_PAYMENT_TIMEOUT_ALREADY_NOTIFIED,including hidden receipts. Fresh recreated auctions/orders/results/bids need new IDs. Separate acceptance from Winner timeout/initial Seller result;does not require those receipts or remove their history. Same timeout sourceEventId may be used for Winner and Seller with independent retry;response only acknowledges Seller,not both recipients complete. Avoid generic cancellation/dispatch for same Seller timeout.

## Persistence/channels/content
AuctionSellerPaymentTimeoutDispatch BaseEntity<Guid>,event PK,unique auction/result/bid/order/notification,hash64,softdelete,restrictive Notification FK. Event→auction→order→result→bid advisory locks seed8019,template FOR SHARE. Atomic receipt+group/delivery/log/AuctionSellerPaymentTimeoutAccepted20 audit (0..19 stable). No raw request/title/contact/Winner identity in audit.
Reuse NotificationType.AuctionPaymentTimedOut9 for same business event,not new enum. No change default matrix33 pairs/12mandatory. One Seller logical item optional Push/InApp per-channel preferences/mandatory pair,global disable wins. Existing type9 mandatory Email applies only Email;this endpoint adds no Email delivery or mandatory Push. New optional vi-VN AUCTION_SELLER_PAYMENT_TIMEOUT_PUSH template ID60000000-0000-0000-0000-000000000016,type9/Push. No/over100 devices skips only Push;allowed InApp retained,Push-only active owner detail,all skipped hidden. Worker/FCM/common retry unchanged;plain parent snapshot preserved on retry/template edits.
Content states Winner missed original deadline and source-recorded order cancellation/auction failure at TimedOutAt;asks Seller check current auction/order. No Winner identity/contact,claimed points deducted/fine,stock released/refund/recreated-auction eligibility or shipping instruction. Payload type AuctionPaymentTimedOut,audience Seller,auction/result/bid/order,settledAt/paymentDeadline/timedOutAt,OrderStatus Cancelled/AuctionStatus Failed;no Winner identity/price/penalty. Client renders plain text and checks current permissions/state in target APIs.

## Responses/errors/enums
Response sourceEventId,auctionId,orderId,notificationId,userId,notificationStatus,inboxVisible,isReplay,deliveries[id,channel,status,errorCode]. Pending=durable queue,InApp Sent=persisted,Push Sent=provider accepted not read or proof domain state.
400 VAL_INVALID_INPUT/TEMPLATE_DATA_MISSING;401 UNAUTHORIZED;403 FORBIDDEN POST;404 TEMPLATE_NOT_FOUND/NOTIFICATION_NOT_FOUND;409 AUCTION_SELLER_PAYMENT_TIMEOUT_CONFLICT/AUCTION_SELLER_PAYMENT_TIMEOUT_ALREADY_NOTIFIED/AUCTION_SELLER_PAYMENT_TIMEOUT_TEMPLATE_INVALID/CHANNEL_NOT_FOUND/TEMPLATE_INVALID/DELIVERY_STATE_INVALID;413 REQUEST_BODY_TOO_LARGE;persistence500 rollback. Skip CHANNEL_DISABLED/PREFERENCE_DISABLED/PUSH_NO_ACTIVE_DEVICES/PUSH_DEVICE_LIMIT.
NotificationType.AuctionPaymentTimedOut9,AuditAction.AuctionSellerPaymentTimeoutAccepted20,Email0/Push1/InApp2,ScopeAll0;DeliveryStatus Pending0/Sending1/Sent2/Failed3/Skipped4;NotificationStatus Pending0/Completed1/Partial2/Failed3;API PascalCase.

## Plan/context/impact/verification
Read project AGENTS/rules/specs and new-feature-endpoint/service-api-doc/controller-conventions workflows,existing SellerResult/WinnerTimeout code/DTO/config/tests and SRS F4 timeout. Pre-edit GitNexus AuditAction/DI UNKNOWN (manually follow enum/framework edges),DbContext MEDIUM,interface/seed LOW;stale index one commit behind. Significant recipient/current-state wording checked manually;no shared worker/enum-type changes.
- [x] Contract/plan before C#.
- [x] Receipt/config/audit20/DbSets/DI,DTO/service/controller/one template.
- [x] Additive migration/designer/snapshot;review table/FK/5 unique indexes/one seed.
- [x] Real PG/Kestrel tests early/status/time/input/auth/size/privacy/grouping/policy/owner/read/device/retry/replay/aliases/races/rollback/template locks/model/Swagger and independent Winner/Seller history.
- [x] Full Release build0errors/warnings/full474 regressions,Git diff/GitNexus,docs/handoff/checklists,owned PG cleanup.

Rollout reviewed migration/seed ID/code+locale collisions,all readers understand audit20;existing API/SDK/worker must support type9/FCM owner detail. Project DB untouched. Real source/client/FCM/latency/receipt retention remain pending;provider crash duplicates possible. No credentials/live sends/Git index/commit/deploy. Next backend gap: auction-start Seller notice per SRS F4-05,then post-payment auction order flow;core integrations still pending.


## Seller auction payment timeout — 2026-11-08

Completed Notification-side POST /api/v1/notifications/auction-seller-payment-timeouts Service/Admin,16KiB/no-store,200 AuctionSellerPaymentTimeoutResponse,GET /{notificationId} authenticated owner-only,no auto-read,foreign/deleted/non-feature404. SRS F4 payment timeout:Seller receives notice without Seller penalty. Strict required event/auction/result/bid/order/Seller/Winner IDs,distinct recipients,title,original settled/deadline/timeout times and declared Order Cancelled/Auction Failed. Rejects future deadline/wrong states/time ordering,unknown contacts/money/penalty/secret/stock flags. Exact deadline allowed only with declared final states;source must resolve payment/cancel race and confirm committed outcome first. Notification validates shape,not actual source state/ownership;source owns24h rule,audience and durable immutable REST retry. No order cancellation,inventory release,reputation/fine/refund/payment/recreation mutation or claim.

Independent AuctionSellerPaymentTimeoutDispatch receipt(event PK,unique auction/result/bid/order/notification,hash64,softdelete,restrictive FK),advisory locks event→auction→order→result→bid seed8019,shared template lock,atomic group/deliveries/log/AuctionSellerPaymentTimeoutAccepted20 audit(old0..19 stable). Same canonical event replay current states;changed409 AUCTION_SELLER_PAYMENT_TIMEOUT_CONFLICT,new event alias reusing any outcome identity409 AUCTION_SELLER_PAYMENT_TIMEOUT_ALREADY_NOTIFIED including hidden receipts. Does not require earlier Winner/Seller result receipt or erase history. Same timeout sourceEventId can reach Winner and Seller through independent endpoints;response acknowledges Seller only,not both recipient completion.

Reuse NotificationType.AuctionPaymentTimedOut9,no new type/pref count:33 effective pairs/12mandatory unchanged. One logical Seller item optional Push/InApp;active mandatory pair affects own channel only,global disable wins. Existing mandatory Email for Winner does not force Seller Push or add Email. New optional vi-VN AUCTION_SELLER_PAYMENT_TIMEOUT_PUSH template600...016. No/over100 devices skips only Push,allowed InApp retained;Push-only active owner detail/all-skipped hidden. Plain parent snapshot,common worker/FCM unchanged. Body states original deadline and cancelled/failed outcome as of source time,asks check current auction/order,no Seller penalty or stock/recreation promise. Payload type AuctionPaymentTimedOut,audience Seller,auction/result/bid/order/times/statuses;no Winner identity/contact/price. Client renders plain text and rechecks target permissions/current state.

Migration20261108141450_AddAuctionSellerPaymentTimeoutNotifications adds receipt table/restrictive FK/5 unique indexes/one template;Up additive,no prior data mutation,designer/snapshot synchronized. Only isolated test databases migrated. Review target template-ID/code+locale collision,migration and audit20 consumers before rollout;type9-compatible API/SDK/worker and owner routing required. Real source/client/FCM receipt/latency,receipt retention and provider crash duplication remain pending. Project DB/credentials/live sends/Git index/commit/deploy untouched.

Release0 errors/warnings;497 tests passed,0failed/skipped,474 retained+23 new real PG/Kestrel cases with real DeliveryService/fake provider. Covers Seller-only grouping/privacy/content,early/exact/historical deadline and declared status checks,strict fields/auth/size,canonical replay/proof/hidden keys/alias and recipient races,new outcomes,optional/mandatory/global/device policies,owned detail/read-badge/revoked device,transient/permanent worker errors and immutable retry,rollback/template errors/shared row lock/generic collision/corrupted delivery state/model/Swagger and preserved initial Winner/Seller+both timeout histories. Existing count fixtures unchanged. GitNexus pre-edit DbContext MEDIUM/interface+seed LOW/audit+DI UNKNOWN manually followed;aggregate tracked scope MEDIUM(15files/20symbols),new files/framework edges manually reviewed,index one commit behind. Owned disposable PG stopped after verification. Handoff docs/api/AuctionSellerPaymentTimeouts/AUCTION_SELLER_PAYMENT_TIMEOUTS.md. Remaining backend gap includes SRS F4-05 Seller auction-start notice and post-payment auction-order flow;real core integrations remain pending.


## Example and handoff

Synthetic source-confirmed historical request (do not invent status facts to match this example):
```json
{
  "sourceEventId": "11111111-1111-1111-1111-111111111111",
  "auctionId": "22222222-2222-2222-2222-222222222222",
  "resultId": "33333333-3333-3333-3333-333333333333",
  "winningBidId": "44444444-4444-4444-4444-444444444444",
  "orderId": "55555555-5555-5555-5555-555555555555",
  "winnerUserId": "66666666-6666-6666-6666-666666666666",
  "sellerUserId": "77777777-7777-7777-7777-777777777777",
  "auctionTitle": "Demo auction",
  "orderStatus": "Cancelled",
  "auctionStatus": "Failed",
  "settledAt": "2026-11-06T05:00:00Z",
  "paymentDeadline": "2026-11-07T05:00:00Z",
  "timedOutAt": "2026-11-07T05:01:00Z"
}
```

Controller: Notification.Api/Controllers/Public/AuctionSellerPaymentTimeoutsController.cs. Application: Notification.Application/Services/AuctionSellerPaymentTimeouts/. Entity: Notification.Domain/Entities/AuctionSellerPaymentTimeoutDispatch.cs. Persistence: Notification.Infrastructure/Persistence/. Tests: Notification.Tests/AuctionSellerPaymentTimeoutApiIntegrationTests.cs. Request/response/RBAC/errors/enums above define handoff. Never dispatch generic cancellation or generic Push for the same Seller timeout.
