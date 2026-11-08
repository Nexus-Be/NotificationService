# Auction Winner/AuctionSettled notification
Status: VERIFIED — local backend automated tests,2026-11-08; Auction/Commerce/client/provider integration pending.

## Scope/API
POST /api/v1/notifications/auction-winners,Service/Admin trusted Gateway,200 pure AuctionWinnerResponse,16KiB/no-store,only after Auction commits final winning result. Winner only;Seller result notification,cancel/timeout remain separate pending steps. Source owns winner/valid bid/result/contact/price/deadline and reliable REST retry. Notification does not settle auctions,create Commerce orders,verify payment,charge/penalize or claim paid. REST only.

GET /api/v1/notifications/auction-winners/{notificationId},authenticated owner,200 InAppNotificationItemResponse;anonymous401,foreign/deleted/non-winner404. Push/Email-only nondeleted owner detail allowed;GET does not mark read. Client resolves payload to Auction/Commerce details and enforces current permissions/order state. No arbitrary payment URL/QR/bank details accepted or generated.

## Input
Required nonempty GUID sourceEventId,auctionId,resultId,winningBidId,winnerUserId,sellerUserId;winner!=seller. WinnerEmail valid<=254;WinnerFullName optional<=200/default Customer;AuctionTitle nonblank<=200. WinningAmount decimal18,2 positive;Currency VND. SettledAt UTC<=now+30sec;PaymentDeadline>SettledAt provided by source (source enforces24h,Notification never resets). Optional OrderId nonempty when present,not required because AuctionSettled can precede Commerce order creation. Optional TraceId<=100;no controls in text. Strict unknown JSON,no password/OTP/payment link/token/private bank/document payload.

Canonical proof includes normalized title/name/lower email/money fixed2digits,IDs,orderId,UTC microsecond times;trace excluded. Source retries immutable snapshot,not enriching null OrderId or looking up mutable deadline/title. Historical/late events allowed;content describes result/deadline at source time and requires checking current order eligibility before payment,not a claim of currently unpaid/running auction. Winning bid price is not necessarily final Commerce amount with shipping/fees.

## Persistence and channels
AuctionWinnerDispatch PK eventId,AuctionId unique,ResultId unique,WinningBidId unique,NotificationId unique restrictive FK,RequestHash64/base/softdelete. Event→auction→result→winning-bid locks seed8015;same event/body replay current statuses,changed409 AUCTION_WINNER_CONFLICT,new event reusing any result identity409 AUCTION_RESULT_ALREADY_NOTIFIED including hidden receipt. One final result per auction;recreated auction uses new auction ID,corrections not overwritten here.

One AuctionWon3/ChannelScope.All NotificationRecord for Winner with plain-text shared Push/InApp content and structured payload(type AuctionWon,auction/result/winningBid/order optional,winningAmount,VND,settledAt,paymentDeadline). Does not expose Seller identity/Winner email in owner payload/response/audit. InApp and Push honor optional preferences unless respective mandatory pair overrides. Email uses dedicated active mandatory AuctionWon/Email template;user cannot opt out while mandatory active,global channel disable always wins. Missing device/over100 skip Push only,not Email/InApp. Parent visible iff permitted InApp,remains active for Email/Push-only;all channels skipped hidden. Provider failures/retries isolated,never rollback source result or erase successful InApp.

New vi-VN templates AUCTION_WINNER_EMAIL(ID600...00f,mandatory Email) and AUCTION_WINNER_PUSH(ID600...010,optional Push). Email HTML renders encoded name/title/price/UTC deadline and instructions to open related auction/order in Nexus,check final amount and valid order state before payment. Optional order reference may be unavailable;no invented URL/auto-order claim. Push/InApp plain text congratulates,states winning price/original deadline,never confirms payment/Seller wallet credit.

Add nullable NotificationDelivery.SubjectOverride<=500/BodyOverride<=4000 for immutable per-delivery Email HTML snapshot. Existing Email worker reads overrides or falls back to parent title/body,Push path unchanged. No client arbitrary override API. New Winner Email delivery sets both;other channels null. Existing deliveries remain null and preserve behavior;manual retry keeps snapshot despite later template edits. Management log/detail does not expose raw HTML/recipient beyond current policy. Receipt+notification/all deliveries/log+AuctionWinnerAccepted16 audit atomic (old0..15 stable),no raw request/full email/body in audit.

## Responses/errors/mappings
Response sourceEventId,auctionId,resultId,notificationId,userId,notificationStatus,inboxVisible,isReplay,deliveries[id,channel,status,errorCode]. Pending DB accepted;InApp Sent persisted;Email/Push Sent provider accepted not read or verified payment. Owner GET returns existing item DTO/payload. FCM protocol unchanged(notificationId/deliveryId/userId).

400 VAL_INVALID_INPUT/TEMPLATE_DATA_MISSING;401 UNAUTHORIZED;403 FORBIDDEN(POST);404 TEMPLATE_NOT_FOUND/NOTIFICATION_NOT_FOUND(GET);409 AUCTION_WINNER_CONFLICT/AUCTION_RESULT_ALREADY_NOTIFIED/AUCTION_WINNER_TEMPLATE_INVALID/CHANNEL_NOT_FOUND/DELIVERY_STATE_INVALID/TEMPLATE_INVALID;413 REQUEST_BODY_TOO_LARGE;unexpected persistence500 rolls back acceptance. Skips CHANNEL_DISABLED/PREFERENCE_DISABLED/PUSH_NO_ACTIVE_DEVICES/PUSH_DEVICE_LIMIT. NotificationType.AuctionWon3,Email0/Push1/InApp2,ScopeAll0;DeliveryStatus Pending0/Sending1/Sent2/Failed3/Skipped4;NotificationStatus Pending0/Completed1/Partial2/Failed3;PascalCase API strings. AuditAction.AuctionWinnerAccepted16. New mandatory AuctionWon/Email pair changes effective preference mandatory count9→10 of24;other auction channels remain optional.

## Pre-code plan/verification
- [x] Contract before C#.
- [x] Receipt/config/DbSet/audit16,DTO/service/controller/DI,2templates.
- [x] Email override fields/config and minimal shared worker fallback;additive migration table/FK/indexes/2nullable columns/2templates/designer/snapshot.
- [x] Real PG/Kestrel tests recipient/privacy/grouping,HTML encoding/Email-vs-Push snapshots/retry/legacy fallback,optional/mandatory/global policies,owner/read/device outcomes,replay/proofs/alias/concurrency/rollback/template locks/late deadline/order absent/model/Swagger.
- [x] Update preference expectation for new mandatory Email pair;Release build/full370 regressions,diff/GitNexus,docs/handoff/history/checklists,owned PG cleanup.

Pre-edit NotificationDelivery CRITICAL,DbContext MEDIUM,seed/contract LOW,DI/worker/framework edges UNKNOWN manually followed;manual HIGH shared Email fallback and financial wording/recipient risks disclosed. No project DB migration/credentials/live SMTP/FCM/source/client integration/commit/deploy. Target seed ID/code+locale collisions must be checked before reviewed rollout. Dedicated receipt retention,real receipt/latency and source/current payment-state validation remain pending. At-least-once provider crash duplicates possible. Next cancellation/timeout;Seller notification remains pending unless specified separately.


## Verified example and handoff

POST example (replace snapshot with committed final source result;orderId may be omitted):

```json
{"sourceEventId":"614f18eb-1b84-4dc1-9660-752d022ba44b","auctionId":"b04348bd-2c8a-4591-8a5d-0f6ac4d7f02c","resultId":"b19f114e-9588-4212-8e18-689eb79b2e4f","winningBidId":"5b042384-d690-4bdb-92f3-029fa8f90c39","winnerUserId":"03929857-c895-4659-9c5c-c79a1587cfa6","sellerUserId":"afee50cd-a82d-46c1-bb98-195107abec18","winnerEmail":"winner@example.com","winnerFullName":"Customer","auctionTitle":"Example auction","winningAmount":1250000.50,"currency":"VND","settledAt":"2026-11-08T11:00:00Z","paymentDeadline":"2026-11-09T11:00:00Z","traceId":"auction-settled-example"}
```

200 response uses one notificationId with InApp Sent,Email Pending and Push Pending per device or Skipped marker. Same canonical event retries same ID/current statuses,no new fanout/audit;changed data409. Source maps naming aliases to stable final result snapshot;missing orderId cannot later be enriched by replaying a changed body. Owner GET payload links auction/result and optional Commerce order only;client fetches current order by allowed domain mapping if not yet created. Never treat this notice as paid/current-unpaid proof or send arbitrary payment links.

Handoff NotificationService;AuctionWinnersController;IAuctionWinnerService/AuctionWinnerService;CreateAuctionWinnerRequest/AuctionWinnerResponse;AuctionWinnerDispatch;NotificationDelivery.SubjectOverride/BodyOverride. POST Service/Admin,GET authenticated owner;DTO/error/enum definitions above. New mandatory Winner Email reflected by preference API;Email global disable may still yield Skipped. Retry of suppressed Skipped does not automatically resume on re-enable;existing failed delivery retry uses operator API and preserves snapshots. Push protocol still notificationId/deliveryId/userId,client uses owner GET and checks current Auction/Commerce state.

Migration20261108114401_AddAuctionWinnerNotifications:nullable delivery columns+table/FK/4 unique indexes+2templates and new-null-field-only seed updates. Up additive reviewed,Down not executed. Apply reviewed migration before new worker reads columns,check target seed-ID/code+locale collisions and existing SMTP/FCM configuration/worker enabled. Upgrade every delivery-worker instance before enabling Winner producers/API traffic: old binaries ignore per-delivery overrides and would use parent plain content. Actual source invocation,real mail/Push receipt/client mapping and latency not verified. No project DB application,credentials edits,live sends,commit/deploy. Dedicated receipt retention remains future policy;at-least-once provider crash duplicates possible.

Validation:Release0 warnings/errors;398 tests pass,0failed/skipped,28 new Winner cases+370 previous. Isolated PostgreSQL/Kestrel/Swagger/DeliveryService with fake providers,model drift false. Winner privacy/content/grouping,HTML snapshots/encoding/legacy fallback,automatic/manual retry,mandatory/global policy,identity/order/deadline validation,owner/read states,replay/concurrency/aliases/rollback/template locks covered. Plan .agents/plan/PLAN-20261108-auction-winners.md. This step not Seller result/cancellation/timeout;those remain pending.
