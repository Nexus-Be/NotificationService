# Seller auction result notification
Status: VERIFIED backend — 2026-11-08; source/client/provider integration pending.

## Objective and sources
Implement Seller result notices for WinnerSelected and NoBids after source commits initial final auction result. SRS F4-06/F4-08 require Seller notified when winner/order exists or no-bid failure. REST only; do not change Winner notification or source business state. Seller payment-timeout and post-payment acceptance/shipping notices are separate pending flows. Source verifies actual ownership/bid/result/order/contact state and reliably retries immutable snapshot. Notification does not settle auction/create order/verify payment/credit wallet/release inventory or guarantee permission to recreate auction.

## API/strict input
POST /api/v1/notifications/auction-seller-results Service/Admin trusted Gateway,16KiB,no-store,200 pure AuctionSellerResultResponse. GET /{notificationId} authenticated owner only,200 InAppNotificationItemResponse;anon401/foreign/deleted/non-feature404;Push-only active detail allowed,GET no read mutation. No list pagination needed.

Required nonempty sourceEventId,auctionId,resultId,sellerUserId,auctionTitle nonblank<=200,decidedAt<=serverNow+30s,traceId optional<=100;no controls. Required outcome string case-insensitive WinnerSelected or NoBids. WinnerSelected requires nonempty winningBidId,winnerUserId!=sellerUserId,orderId,winningAmount positive decimal18,2,currency VND,paymentDeadline>decidedAt normalized UTC microseconds. Order required because source calls Seller notice after order creation as F4-08;before payment,not an instruction to accept/ship. Historical deadline allowed and never reset. NoBids requires all winner/bid/order/amount/currency/deadline fields absent/null (source confirms no valid bids);does not claim stock release or automatic re-create. Strict unknown JSON rejects addresses,email,phone,bank,OTP/password,penalty,paid/credit/stock flags and arbitrary URLs. Only source-declared shape validated here,not independent proof.

Canonical proof normalized IDs/title/outcome/amount fixed2digits/VND/UTC microsecond times,trace excluded. Same event/body replays current state,changed409 AUCTION_SELLER_RESULT_CONFLICT. New event reusing auction/result/winningBid/order409 AUCTION_SELLER_RESULT_ALREADY_NOTIFIED,keys retained through hidden receipts. One initial result per auction across both outcomes,corrections not overwrite;recreated auction/result/order/bid fresh IDs. Separate receipt from Winner allows independent retry,shared sourceEventId allowed with distinct recipient;does not prove both audiences complete or mutate prior notices.

## Domain/persistence/channels
AuctionSellerResultDispatch BaseEntity<Guid>,event PK,auction/result/notification unique,nullable winningBid/order unique (Postgres multiple nulls),hash64,soft delete,restrictive Notification FK. Event→auction→result→bid(if present)→order(if present) locks seed8018;template FOR SHARE;atomic notification/deliveries/log/receipt/AuctionSellerResultAccepted19 audit (0..18 stable). No raw request or winning buyer identity in payload/audit. One NotificationType.AuctionSellerResult10 item,previous0..9 stable. Default preference matrix33 pairs,12 mandatory;Push/InApp optional unless own mandatory pair,global disable wins. No Email delivery in this flow;no change common worker/FCM protocol. No/over100 devices skips Push only;allowed inbox retained. Push-only active,all skipped hidden;provider failure/retry isolated.

Templates vi-VN AUCTION_SELLER_WINNER_SELECTED_PUSH ID60000000-0000-0000-0000-000000000014 and AUCTION_SELLER_NO_BIDS_PUSH ID...0015,optional Push/type10. Plain text/title<=500,body<=4000,render user values as plain text;client safely renders text. Winner text states result as of decidedAt,winning bid price and original deadline,asks check current auction/order;no paid/wallet credit/shipping claim. NoBids text states failed because no bids at decidedAt,asks check current auction;no guarantee stock/recreation. Payload type AuctionSellerResult,outcome,auctionId,resultId,orderId/bid/amount/currency/deadline nullable,decidedAt;no Winner identity/email or other participants.

## Responses/errors/enums
Response sourceEventId,auctionId,resultId,notificationId,userId,notificationStatus,inboxVisible,isReplay,deliveries[id,channel,status,errorCode]. Pending is durable queue;InApp Sent=persisted,Push Sent=provider accepted not read/domain validation.
400 VAL_INVALID_INPUT/TEMPLATE_DATA_MISSING;401 UNAUTHORIZED;403 FORBIDDEN POST;404 TEMPLATE_NOT_FOUND/NOTIFICATION_NOT_FOUND;409 AUCTION_SELLER_RESULT_CONFLICT/AUCTION_SELLER_RESULT_ALREADY_NOTIFIED/AUCTION_SELLER_RESULT_TEMPLATE_INVALID/CHANNEL_NOT_FOUND/TEMPLATE_INVALID/DELIVERY_STATE_INVALID;413 REQUEST_BODY_TOO_LARGE;unexpected persistence500 rolls back. Skip CHANNEL_DISABLED/PREFERENCE_DISABLED/PUSH_NO_ACTIVE_DEVICES/PUSH_DEVICE_LIMIT.
NotificationType.AuctionSellerResult10,AuditAction.AuctionSellerResultAccepted19,Email0/Push1/InApp2,ScopeAll0;DeliveryStatus Pending0/Sending1/Sent2/Failed3/Skipped4;NotificationStatus Pending0/Completed1/Partial2/Failed3. APIs PascalCase strings.

## Plan/context/impact/verification
Read AGENTS,rules,specs,endpoint/API-doc/controller skills,Winner/Outbid/Timeout features/entities/DTOs/config/tests and source SRS F4. GitNexus pre-edit DbContext MEDIUM,SeedAll LOW,enums/interface/DI UNKNOWN with framework/enum dependencies manually followed;index one commit behind. No common worker changes. Manual significant risk financial wording and recipient/privacy checked explicitly.
- [x] Contract and plan before code.
- [x] Add receipt/config/type/audit/DbSets/DI,DTO/service/controller,2templates.
- [x] Additive migration/designer/snapshot;review Up/FK/5 unique indexes/2seeds.
- [x] Real PostgreSQL/Kestrel tests both outcomes,strict conditional input/auth,size,privacy/grouping/owner/read-badge,policy/device/worker failures/retry,canonical replay/aliases/concurrency/rollback/template locking/render/model/Swagger,independence from Winner.
- [x] Full Release build0errors/warnings/full449 regressions,Git diff/GitNexus,update docs/history/checklists,stop owned test DB.

Rollout:review/apply migration and template ID/code+locale collisions;upgrade API/SDK/worker enum readers before enabling source. Project DB untouched. Source/client/provider real integration,latency,receipt retention and at-least-once crash duplication remain pending. No credentials/live sends/commit/deploy.


## Seller auction results — 2026-11-08

Implemented Notification-side POST /api/v1/notifications/auction-seller-results (Service/Admin,16KiB,no-store,200 AuctionSellerResultResponse),GET /{notificationId} owner-only (200 InAppNotificationItemResponse,anonymous401/foreign/deleted/non-feature404,no auto-read). SRS F4-06/F4-08 branches WinnerSelected and NoBids. Required immutable event/auction/result/Seller/title/decidedAt;WinnerSelected requires distinct Winner,bid,created Commerce order,decimal18,2 positive VND price,original deadline after decidedAt. NoBids rejects winner/bid/order/amount/currency/deadline data. Unknown JSON and sensitive/contact/payment/stock flags rejected. Historical outcomes keep source times;Seller result after order creation,independent from earlier Winner notice. Does not confirm payment,wallet credit,stock release,recreation eligibility or instruct acceptance/shipping before actual paid flow. Source owns actual ownership,result/order/bid facts and durable immutable REST retry;Notification validates declared shape,never settles/creates/pays/cancels/recreates source resources.

AuctionSellerResultDispatch event PK,unique auction/result/notification plus nullable unique bid/order,softdelete,restrictive notification FK,hash64. Event→auction→result→bid→order locks8018,shared template lock;atomic receipt/log/group/delivery/AuctionSellerResultAccepted19 audit. Same event/body replay,changed409 AUCTION_SELLER_RESULT_CONFLICT;new event reusing auction/result/bid/order409 AUCTION_SELLER_RESULT_ALREADY_NOTIFIED even hidden. One initial result across both outcomes;fresh re-created auctions use new IDs. Separate Winner/Seller receipts allow same source event with independently retried distinct recipients;no assertion both notifications complete.

One AuctionSellerResult10 logical item with optional Push fanout and InApp,global disable wins,existing mandatory pair policy applies only matching channel. New vi-VN optional templates AUCTION_SELLER_WINNER_SELECTED_PUSH ID600...014 and AUCTION_SELLER_NO_BIDS_PUSH ID600...015. Plain text parent snapshots;safe text rendering required in clients. Payload has outcome/auction/result/order/bid/price/currency/source times nullable by branch,no Winner identity/contact or other participants. No Email delivery added,no shared worker/FCM changes. Missing/over100 devices skips only Push,permitted inbox retained,Push-only owner detail available,all skipped hidden;provider errors/retry preserve inbox and original text. Appended type10/audit19 preserve previous enum values;effective preference defaults33 pairs/12 mandatory (30/12 previously),known count fixtures intentionally updated.

Migration20261108140454_AddAuctionSellerResultNotifications adds receipt table/restrictive FK/5 unique indexes/2templates,designer/snapshot synchronized,Up additive no previous data mutation. Applied only in isolated PostgreSQL tests. Target migration,seed ID/code+locale collision checks and enum-compatible API/SDK/worker rollout required before source enable. Real source/client/FCM receipt/latency,receipt retention and at-least-once provider crash duplication remain pending. Seller payment-timeout and post-payment order acceptance/shipping are separate pending flows.

Release0 errors/warnings;474 tests passed,0failed/skipped,449 retained+25 new real PG/Kestrel cases. New suite covers both outcomes,conditional validation/auth/size/unknown secrets,recipient privacy/no premature financial claims,grouping/owner/read-badge/Push-only access,optional/mandatory/global/device policies,worker permanent/transient failure/snapshot retry/revoked devices,canonical replay/hidden keys/event and outcome races/recreated auctions,atomic rollback/template errors/shared row locking/generic collision/model/Swagger,and independent Winner/Seller receipts. Initial test scaffold corrected to actual DispatchService and existing single-item read-state route;no existing endpoint changed. GitNexus pre-edit DbContext MEDIUM/seed LOW,enums/interface/DI UNKNOWN reviewed manually;tracked aggregate after implementation MEDIUM(15files/18symbols,index one commit behind,new files/framework reviewed manually). Existing unrelated changes preserved. No project DB/credentials/live sends/index/commit/deploy changes. Contract/handoff docs/api/AuctionSellerResults/AUCTION_SELLER_RESULTS.md. Owned disposable PostgreSQL stopped after final verification.


## Request examples

WinnerSelected (historical times accepted; not a current unpaid assertion):
```json
{
  "sourceEventId": "11111111-1111-1111-1111-111111111111",
  "auctionId": "22222222-2222-2222-2222-222222222222",
  "resultId": "33333333-3333-3333-3333-333333333333",
  "sellerUserId": "44444444-4444-4444-4444-444444444444",
  "outcome": "WinnerSelected",
  "auctionTitle": "Demo item",
  "decidedAt": "2026-11-08T05:00:00Z",
  "winningBidId": "55555555-5555-5555-5555-555555555555",
  "winnerUserId": "66666666-6666-6666-6666-666666666666",
  "orderId": "77777777-7777-7777-7777-777777777777",
  "winningAmount": 150000,
  "currency": "VND",
  "paymentDeadline": "2026-11-09T05:00:00Z"
}
```

NoBids uses the common required fields with outcome NoBids and omits all winner/bid/order/price/currency/deadline fields. These are distinct alternative outcomes for an auction,not sequential requests for the same auction. Synthetic IDs only.

Controller: Notification.Api/Controllers/Public/AuctionSellerResultsController.cs. Application: Notification.Application/Services/AuctionSellerResults/. Entity: Notification.Domain/Entities/AuctionSellerResultDispatch.cs. Config/migration: Notification.Infrastructure/Persistence/. Tests: Notification.Tests/AuctionSellerResultApiIntegrationTests.cs. RBAC,request,response,errors and enum mappings above are the handoff contract. Client opens Auction/Commerce details through its own authorized routes and renders content as text. Source must avoid generic dispatch for the same Seller event.
