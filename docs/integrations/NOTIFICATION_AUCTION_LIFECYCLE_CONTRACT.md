# Auction lifecycle notifications through Gateway

> **Source service:** AuctionService (`8005`)
> **Downstream:** API Gateway → NotificationService (`8007`)
> **Status:** Authentication and transport synchronized with the current implementation.
> **Scope:** No auction-created notification is sent. The three new lifecycle events are delivered in-app. Email and Push for these events are deferred.

## Transport

AuctionService sends asynchronous HTTP `POST` requests to the configured `Notification:BaseUrl`. In deployment, this URL must be the API Gateway base URL, not a direct NotificationService URL.

The Gateway must forward the following routes to NotificationService without changing the JSON body:

| Purpose | Route |
|---|---|
| Auction failed | `POST /api/v1/notifications/auction-failed` |
| Auction cancelled | `POST /api/v1/notifications/auction-cancelled` |
| Replacement winner offer | `POST /api/v1/notifications/auction-winner-offers` |

### Headers and authentication

AuctionService sends:

```http
Content-Type: application/json
Authorization: Bearer <auction-service-token>
```

| JWT setting | Value |
|---|---|
| Algorithm | HS256 with a dedicated secret of at least 32 UTF-8 bytes |
| Issuer | `auction-service` |
| Audience | `nexus-auction-notifications` |
| Subject (`sub`) | Auction's configured `Notification:ServiceUserId`, in canonical GUID format |
| Scope | `auction.notifications.write` |
| Lifetime | 2 minutes, with 15 seconds of clock skew at validation |

Gateway authenticates the service token for the three POST routes above and the existing POST routes `/api/v1/notifications/auction-outbids` and `/api/v1/notifications/auction-winners`. It forwards the JSON body, trace context and bearer token. Gateway derives `x-user-id` and `x-user-roles: service` from the verified service identity and sets `X-Caller-Service: auction-service` downstream. Auction sends no identity headers to Gateway. NotificationService independently validates the token for the three lifecycle endpoints, including direct calls. Missing or invalid credentials return 401. Valid credentials without the required service identity or scope return 403.

### Docker Compose configuration

| Variable | Where to set it | Value |
|---|---|---|
| `AUCTION_NOTIFICATION_SIGNING_KEY` | Auction, Gateway, Notification | The same dedicated secret in all three services. Supply it through private environment configuration. |
| `AUCTION_NOTIFICATION_SERVICE_USER_ID` | Auction, Gateway, Notification | The same canonical GUID in all three services. The Compose default is `00000000-0000-0000-0000-0000000000a1`. |
| `AUCTION_NOTIFICATION_BASE_URL` | Auction | `http://nexus-gateway:8080` on the shared Docker network. |
| `AUCTION_NOTIFICATION_ENABLED` | Auction | Set to `true` after Gateway and Notification are ready with the same credentials. |

## Common delivery requirements

- `sourceEventId` is the idempotency key for the notification event.
- NotificationService must treat a repeated `sourceEventId` as the same event and must not send a duplicate user notification.
- Return any `2xx` status when the notification has been accepted.
- Return `4xx` for a permanent validation/authorization failure.
- Return `408`, `429`, or `5xx` for a temporary failure. AuctionService makes one initial attempt and up to three retries. Network errors and timeouts are also retried. Each attempt uses the same payload and source event ID.
- AuctionService does not send an auction-created notification.
- The current AuctionService queue is in memory. A queued notification can be lost when AuctionService restarts; this contract does not introduce an outbox.

## 1. Auction failed

### Route

```http
POST /api/v1/notifications/auction-failed
```

### Recipient

The seller identified by `sellerUserId`.

### JSON body

```json
{
  "sourceEventId": "guid",
  "auctionId": "guid",
  "sellerUserId": "guid",
  "auctionTitle": "Product title",
  "reason": "Auction ended without a valid bid.",
  "failedAt": "2026-11-06T14:30:00Z",
  "traceId": "trace-id"
}
```

### Semantics

Send when the persisted auction status changes to `Failed`, including:

- The auction ends without a valid bid.
- Commerce payment fails/expires and no eligible replacement bidder remains.
- The invited replacement bidder declines or expires and the auction becomes failed.
- The seller rejects the paid order or does not respond in time.

The event ID is deterministic for the auction status transition:

```text
MD5("auction-failed:{auctionId:N}:{failedAt:O}")
```

## 2. Auction cancelled

### Route

```http
POST /api/v1/notifications/auction-cancelled
```

### Recipient

The seller identified by `sellerUserId`.

### JSON body

```json
{
  "sourceEventId": "guid",
  "auctionId": "guid",
  "sellerUserId": "guid",
  "auctionTitle": "Product title",
  "reason": "Seller cancelled the auction.",
  "cancelledAt": "2026-11-06T14:30:00Z",
  "forced": false,
  "traceId": "trace-id"
}
```

`forced` is `true` when an admin force-cancels the auction and `false` for seller cancellation.

The event ID is deterministic for the cancellation transition:

```text
MD5("auction-cancelled:{auctionId:N}:{cancelledAt:O}")
```

## 3. Replacement winner offer

### Route

```http
POST /api/v1/notifications/auction-winner-offers
```

### Recipient

The bidder identified by `bidderUserId`. This is the next eligible bidder after the previous winner's payment failed, expired, or timed out.

### JSON body

```json
{
  "sourceEventId": "guid",
  "auctionId": "guid",
  "offerId": "guid",
  "bidId": "guid",
  "bidderUserId": "guid",
  "previousWinnerUserId": "guid",
  "sellerUserId": "guid",
  "auctionTitle": "Product title",
  "amount": 1250000.00,
  "currency": "VND",
  "offeredAt": "2026-11-06T14:30:00Z",
  "expiresAt": "2026-11-07T14:30:00Z",
  "traceId": "trace-id"
}
```

The message should tell the bidder that they have been invited as the replacement winner and must accept/respond before `expiresAt`. The bidder's existing winner-offer API remains the action endpoint:

```http
POST /api/v1/auctions/{auctionId}/winner-offers/{offerId}/accept
POST /api/v1/auctions/{auctionId}/winner-offers/{offerId}/decline
```

The event ID is deterministic by offer ID:

```text
MD5("auction-winner-offer:{offerId:N}")
```

## Existing notification routes retained

These routes remain unchanged:

- `POST /api/v1/notifications/auction-outbids`
- `POST /api/v1/notifications/auction-winners`

No notification is sent when an auction is created.
