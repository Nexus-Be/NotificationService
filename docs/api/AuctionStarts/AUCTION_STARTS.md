# Seller auction-start notification
Status: VERIFIED backend — 2026-11-08; source/client/provider integration pending.

## Objective/source boundary
SRS UC-F4-05:source job activates Scheduled→Active at scheduled time and notifies Seller;cancelled auction must never activate. Implement Notification-side acceptance,not scheduling/eligibility/stock/auction mutation. REST only. Source confirms actual ownership/activation commit and current cancellation race,then retries immutable snapshot. Notification validates declared shape,not source truth. AuctionStatus canonical Active (source may map its Running domain state only after actual equivalent activation);not arbitrary Running/Cancelled/Scheduled here. Late historical activation notices never claim auction remains active or invite bidding.

## API/input
POST /api/v1/notifications/auction-starts Service/Admin via trusted Gateway,16KiB/no-store,200 AuctionStartResponse. GET /{notificationId} authenticated owner-only,200 InAppNotificationItemResponse,anonymous401/foreign/deleted/non-feature404,Push-only active detail allowed,no auto-read. No list endpoint.
Required nonempty sourceEventId,auctionId,sellerUserId;auctionTitle nonblank<=200;auctionStatus Active case-insensitive;scheduledStartAt<=startedAt<=serverNow,auctionEndsAt>startedAt. Required source timestamps UTC microseconds;exact serverNow accepted,historical end may be past,do not recompute duration or apply present anti-sniping end to retry. Source validates schedule/duration/cancellation/eligibility,Notification does not infer activation from elapsed time. TraceId optional<=100,no controls. Strict unknown JSON rejects buyer/winner/bid/order/payment/price/contact/stock flags,password/OTP/URL/token. No user-selected destination.

Canonical proof normalized IDs/title/Active/three times,trace excluded. Same event/snapshot replay current deliveries,changed409 AUCTION_START_CONFLICT;new event same auction409 AUCTION_START_ALREADY_NOTIFIED including hidden receipts. One activation notice per immutable auction,not resume events or repeated starts. Recreated auction fresh ID;no edits/corrections overwrite. Independent from Seller result/cancellation/timeout receipts;does not remove history or prove final current state.

## Persistence/channels/content
AuctionStartDispatch BaseEntity<Guid>,event PK,auction/notification unique,hash64,softdelete,restrictive Notification FK. Event→auction advisory locks seed8020,template FOR SHARE,atomic receipt/group/delivery/log/AuctionStartAccepted21 audit(old0..20 stable,no raw title/body/request).
Append NotificationType.AuctionStarted11,old0..10 stable. Default preference matrix36 pairs/12mandatory;update explicit fixtures only. New vi-VN AUCTION_STARTED_PUSH template60000000-0000-0000-0000-000000000017,type11/Push,optional. One logical Seller item with Push per device and InApp optional per-channel preferences/mandatory pairs,global disable wins. No Email,common worker/FCM unchanged. No/over100 devices skips Push only,allowed InApp preserved;Push-only active detail,all skipped hidden. Retry uses original plain parent snapshot. Payload type AuctionStarted,audience Seller,auctionId,scheduledStartAt,startedAt,auctionEndsAt,auctionStatus Active;no participants or money/contact. Text records activation at source time and end planned at activation,asks check current auction;no unconditional running/bid/shipping/stock/revenue claims. Clients safely render plain text and check target permissions/current state.

## Response/errors/enums
Response sourceEventId,auctionId,notificationId,userId,notificationStatus,inboxVisible,isReplay,deliveries[id,channel,status,errorCode]. Pending=durable queue,InApp Sent=persisted,Push Sent=provider accepted not read or source proof.
400 VAL_INVALID_INPUT/TEMPLATE_DATA_MISSING;401 UNAUTHORIZED;403 FORBIDDEN POST;404 TEMPLATE_NOT_FOUND/NOTIFICATION_NOT_FOUND;409 AUCTION_START_CONFLICT/AUCTION_START_ALREADY_NOTIFIED/AUCTION_START_TEMPLATE_INVALID/CHANNEL_NOT_FOUND/TEMPLATE_INVALID/DELIVERY_STATE_INVALID;413 REQUEST_BODY_TOO_LARGE;unexpected persistence500 rollback. Skip CHANNEL_DISABLED/PREFERENCE_DISABLED/PUSH_NO_ACTIVE_DEVICES/PUSH_DEVICE_LIMIT.
NotificationType.AuctionStarted11,AuditAction.AuctionStartAccepted21;Email0/Push1/InApp2,ScopeAll0;DeliveryStatus Pending0/Sending1/Sent2/Failed3/Skipped4;NotificationStatus Pending0/Completed1/Partial2/Failed3;PascalCase API strings.

## Plan/context/impact/verification
Use existing new-feature-endpoint/service-api-doc/controller-conventions workflows. Read AGENTS/rules/specs,nearest SellerTimeout/Outbid/Winner features/config/DTO/tests,SRS F4-05. Pre-edit DbContext MEDIUM/interface+seed LOW,enums+DI UNKNOWN manually follow framework/enum edges,index one commit behind;new files reviewed manually. No shared worker changes.
- [x] Contract/plan before C#.
- [x] Receipt/config/type11/audit21/DbSets/DI,DTO/service/controller/template.
- [x] Additive migration/designer/snapshot:table/FK/2 unique indexes/one seed.
- [x] PG/Kestrel tests source time/status/auth/strict input/size,recipient/privacy/history/grouping,policy/device/owner/read,worker retry/replay/alias/races/rollback/template locks/model/Swagger.
- [x] Full Release0errors/warnings/full497 regressions,Git diff/GitNexus,docs/history/checklists,owned PG cleanup.

Rollout reviewed migration/check seed ID/code+locale collisions,upgrade API/SDK/workers to type11 and audit readers21 before source enable. Real source/client/FCM/latency/retention remain pending;provider crash duplicates possible. No project DB/credentials/live sends/Git index/commit/deploy. Next backend:auction order payment-success notices and Seller acceptance deadline distinct from initial winner result;core integrations remain pending.


## Seller auction activation — 2026-11-08

Implemented POST /api/v1/notifications/auction-starts(Service/Admin,16KiB/no-store,200 AuctionStartResponse),GET /{notificationId}(authenticated owner,200 InAppNotificationItemResponse,no auto-read,anonymous401/foreign/deleted/non-feature404). SRS F4-05 Seller activation notice after source commits Scheduled→Active. Strict nonempty event/auction/Seller/title and source schedule/start/end;scheduledStartAt<=startedAt<=serverNow,end>startedAt,normalized UTC microseconds,declared canonical Active(case-insensitive). Rejects future/early starts,wrong states,unknown participant/order/money/contact/secret/stock fields. Source may map actual equivalent Running state to canonical Active only after activation;Notification does not prove source truth,run jobs,activate/restart/cancel auctions or check ownership/eligibility/inventory. Historical notices preserve source start/planned end;content says activation occurred at that time,not currently open or invitation to bid. Client checks actual target permissions/current state and safely renders plain text.

AuctionStartDispatch event PK,unique auction/notification,hash64,softdelete,restrictive FK. Event→auction locks8020,shared template lock,atomic group/deliveries/log/AuctionStartAccepted21 audit(old0..20 stable). Canonical same event/body replay current state,changed409 AUCTION_START_CONFLICT,new event same auction409 AUCTION_START_ALREADY_NOTIFIED including hidden keys. One activation per auction,not resume/restart;recreated auction uses fresh ID. Independent from later Seller results/cancellations/timeouts,history retained. No generic dispatch duplication for same source event.

Append NotificationType.AuctionStarted11(old0..10 stable),one optional vi-VN AUCTION_STARTED_PUSH template600...017. One logical Seller item optional InApp+Push per-device;per-channel mandatory pair policy/global disable retained,no Email/common worker/FCM changes. No/over100 devices skips only Push,permitted inbox retained;Push-only active detail/all skipped hidden. Parent snapshot/retry/ownership remain existing behavior. Payload type AuctionStarted,audience Seller,auction/schedule/start/planned end/status,no participants/contact/price. Effective defaults36 pairs/12mandatory(previous33/12),explicit count fixtures adjusted only for added enum.

Migration20261108142349_AddAuctionStartNotifications adds receipt table/restrictive FK/2 unique indexes/one seed;Up additive,no old data changes,designer/snapshot match model. Only isolated test DB migrated. Check target template-ID/code+locale collision and upgrade all API/SDK/worker readers to type11/audit21 before source enable. Project DB migration/source/client/provider/latency/receipt retention remain pending;Sent only provider accepted,crash duplication possible. No credentials/live sends/Git index/commit/deploy changes.

Release0 errors/warnings;519 tests passed,0failed/skipped(497 retained+22 new PG/Kestrel cases using real DeliveryService/fake provider). Covers Seller-only grouped item/privacy/time-aware content,source status/time/boundaries/strict fields/auth/size,canonical replay/hidden keys/proof/aliases/recipient races,new auction,optional/mandatory/global/device policies,owner/read-badge/Push-only/revoked devices,worker failure/automatic snapshot retry,atomic rollback/template errors/shared row lock/generic collision/corrupted manifest/model/Swagger and later result history preserved. GitNexus pre-edit DbContext MEDIUM/interface+seed LOW/enums+DI UNKNOWN manually followed,index stale one commit;tracked aggregate MEDIUM(15files/21symbols),new files/framework reviewed manually. Disposable owned PG stopped after final verification. Contract/handoff docs/api/AuctionStarts/AUCTION_STARTS.md. Next backend:auction order paid/Seller acceptance deadline,distinct from winner selection;real core integration still pending.


## Example and handoff

Synthetic historical request;source must confirm actual activation and Seller ownership,not invent facts from this example.
```json
{
  "sourceEventId": "11111111-1111-1111-1111-111111111111",
  "auctionId": "22222222-2222-2222-2222-222222222222",
  "sellerUserId": "33333333-3333-3333-3333-333333333333",
  "auctionTitle": "Demo auction",
  "auctionStatus": "Active",
  "scheduledStartAt": "2026-11-07T05:00:00Z",
  "startedAt": "2026-11-07T05:00:01Z",
  "auctionEndsAt": "2026-11-07T07:00:00Z"
}
```

Controller: Notification.Api/Controllers/Public/AuctionStartsController.cs. Application: Notification.Application/Services/AuctionStarts/. Entity: Notification.Domain/Entities/AuctionStartDispatch.cs. Persistence: Notification.Infrastructure/Persistence/. Tests: Notification.Tests/AuctionStartApiIntegrationTests.cs. Request/response/RBAC/errors/enums above define handoff. Source keeps event/activation snapshot immutable across retries;client routes to authorized auction details and checks current state.
