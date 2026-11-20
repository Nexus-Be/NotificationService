# Auction activity notifications

These POST routes receive events from AuctionService through Gateway using the dedicated Auction JWT policy.

| Route under /api/v1/notifications | Recipient | Input snapshot |
|---|---|---|
| auction-watchlist-bids | Each watcherUserIds entry | sourceEventId, auctionId, triggerBidId, auctionTitle, newAmount, currency, watcherUserIds, occurredAt, auctionEndsAt, optional traceId |
| auction-report-resolved | Each reporterUserIds entry | sourceEventId, auctionId, auctionTitle, outcome (ActionTaken or Dismissed), message, optional adminReason, reporterUserIds, resolvedAt, optional traceId |
| auction-deposits | bidderUserId | sourceEventId, auctionId, depositId, bidderUserId, auctionTitle, amount, currency, depositStatus (Authorized, Released or Captured), optional reason, occurredAt, optional traceId |

All IDs must be nonempty GUIDs. Currency is VND and monetary values are positive with at most two decimal places. Each batch has 1–1000 distinct recipients. Request size is limited to 128 KiB. Source must coordinate batching for larger audiences. Title is limited to 200 characters, message to 1000 and trace to 100.

## Delivery and replay

The current implementation creates in-app notifications. Channel and user disable flags are respected. A hidden record preserves event identity when in-app delivery is disabled.

HTTP 200 returns sourceEventId, isReplay and notificationIds. Retry uses the same event ID and snapshot. Reordering the same recipients or changing trace context preserves the accepted result. Changing business fields or recipients returns 409 AUCTION_ACTIVITY_CONFLICT. Concurrent attempts are serialized at the event ID and commit their entire recipient group atomically. Missing/invalid service tokens return 401; invalid subject or scope returns 403.

Each recipient gets a private payload containing auctionId and event details. Recipient arrays and internal adminReason are excluded from the personal notification payload. For a report, the source should send a recipient-safe message.

## Configuration and verification

Gateway and Notification need the same dedicated signing key and service subject as AuctionService. Source issuer is auction-service, audience is nexus-auction-notifications and scope is auction.notifications.write.

Verify through the actual Auction outbox dispatcher and Gateway. Check one item per intended recipient, replay, concurrent requests, invalid credentials and invalid deposit state. Use isolated PostgreSQL for tests. Web Push and email for these three routes are future work.
