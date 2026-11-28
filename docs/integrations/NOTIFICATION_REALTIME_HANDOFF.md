# Notification realtime handoff for Gateway and FE

Notification remains the owner of durable inbox records. REST callers continue using the existing authenticated event endpoints. The realtime stream is only a hint after a record is committed; it must never be treated as delivery proof.

## Gateway request

- Proxy `/hubs/notifications/{**catch-all}` to NotificationService, including `POST /negotiate` and WebSocket upgrade, without rewriting the path or response envelope.
- Allow only configured FE origins on HTTPS/WSS. Preserve the User JWT for negotiate; a browser WebSocket may send it as `access_token` in the query **only** on `/hubs/notifications`. Do not log the query token.
- Keep service event POST routes protected; the hub gives no permission to create notifications.

## FE request

- Connect the authenticated Buyer/Seller session to `/hubs/notifications`. The hub accepts no client methods and uses the validated JWT subject as the user ID.
- Listen for `NotificationCreated` with `{notificationId, type, createdAt}`. For `AuctionOutbid`, refresh unread count and show a short alert with a link to the auction. Fetch the full notification via the existing owned REST API; do not trust a client-supplied user ID.
- On reconnect, visibility/focus and account switch, call `GET /api/v1/notifications/me` and reconcile by `notificationId`. Clear the old connection and cache on logout. Do not mark read when a toast appears.
- Continue polling as fallback. Render Free can sleep and WebSocket delivery may be delayed or disconnected; durable inbox catch-up is mandatory. Browser push while the web app is closed is a separate feature.

## Acceptance

With Buyer A and Buyer B, an outbid for A reaches only A while both are connected. Duplicate event replay does not create a second toast or DB record. When A disconnects before two outbids and reconnects, the inbox shows all owned notices in order. A forged/expired JWT and a request without a JWT cannot join the hub.

Current Notification implementation emits `NotificationCreated` for newly persisted visible `auction-outbids`. Other business types can be added after their caller/callee flows pass the same tests.
