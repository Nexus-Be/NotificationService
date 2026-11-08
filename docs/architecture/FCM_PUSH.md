# FCM Push setup and delivery behavior
Status: VERIFIED by automated tests; real-device validation pending, 2026-11-07.

Use FCM HTTP v1 with short-lived OAuth bearer tokens from Google Application Default Credentials. Set Fcm:ProjectId / FCM_PROJECT_ID and provide server credentials using workload identity or GOOGLE_APPLICATION_CREDENTIALS pointing to a mounted service-account JSON outside the repository. Enable FCM HTTP v1 API and grant the server permission to send in the target project. Never paste private keys into chat or put them in appsettings/.env/source control.

Android, iOS and Web clients must register real FCM registration tokens through the existing devices endpoint. iOS requires Firebase/APNs project configuration; Web requires Firebase client/service worker setup. Raw APNs tokens and arbitrary web push subscriptions are not interchangeable with FCM registration tokens.

Backend sends one HTTP message per delivery, carrying notificationId, deliveryId and userId in data. No automatic Firebase Admin SDK delivery retries are used, so each worker claim performs at most one FCM request. OAuth token acquisition has its own library behavior. Quota/unavailable/internal errors retry; quota backoff starts at 60 seconds and doubles (60s/120s by default), and Retry-After is honored when longer. UNREGISTERED marks the matching token snapshot inactive; payload INVALID_ARGUMENT and SENDER_ID_MISMATCH are permanent but do not invalidate the token automatically. Revocation/reassignment before send causes Skipped. Messages already in flight/accepted cannot be recalled; clients must manage tokens at logout/account changes and can use deliveryId for duplicate suppression. Sent means FCM accepted the request, not that the device displayed it.

Official references:
- https://firebase.google.com/docs/cloud-messaging/send/v1-api
- https://firebase.google.com/docs/cloud-messaging/error-codes
- https://www.nuget.org/packages/Google.Apis.Auth/1.78.0

Tests must not contact Google/FCM or use real credentials. Document any remaining real-device validation gap explicitly.


## Server setup
1. Set FCM_PROJECT_ID (or Fcm:ProjectId). The server identity must have permission to send FCM messages for that project.
2. On managed infrastructure, use its ADC/workload identity. On a machine/container, store a service-account JSON outside this repo and mount it read-only; set GOOGLE_APPLICATION_CREDENTIALS to the file path visible inside the running process.
3. For Compose, the base file passes FCM_PROJECT_ID and FCM_CREDENTIALS_CONTAINER_PATH. It does not mount a secret automatically. An optional local Compose override can contain:

```yaml
services:
  notification-service:
    environment:
      GOOGLE_APPLICATION_CREDENTIALS: /run/secrets/firebase-service-account.json
    volumes:
      - type: bind
        source: "${FCM_CREDENTIALS_HOST_PATH:?Set credential path outside the repository}"
        target: /run/secrets/firebase-service-account.json
        read_only: true
```

4. Register actual FCM tokens from clients with the existing devices endpoint. Use a Push template with Destination omitted. No Firebase key should be placed in an API request.
5. Missing project/credentials produces a terminal configuration failure for the delivery, not simulated success. Configure Firebase before requesting real Push dispatches. Runtime configuration is read at startup; restart after setup changes.

## Implementation and persistence
- Infrastructure/Services/Push/: FcmOptions, IFcmAccessTokenProvider, GoogleFcmAccessTokenProvider and FcmPushDeliverySender. Google.Apis.Auth 1.78.0 supplies ADC/OAuth; FCM delivery uses one ordinary HTTP v1 request per worker attempt.
- Application/Interfaces/IPushDeliverySender.cs and Services/Deliveries/PushDeliveryDtos.cs define the boundary. DeliveryService uses a common internal outcome for Email/Push while preserving the existing Email sender contract and constructor calls.
- DispatchService snapshots active owned devices (maximum 100); DispatchResponse adds deliveries while retaining top-level first-delivery fields. Full replay manifests remain stable across later device registrations.
- Push Destination in NotificationDeliveries is the DeviceToken.Id string, so no token-width/schema migration is needed. Before each attempt, resolve that active device for the original recipient. Revoked/reassigned/deleted devices are Skipped without calling FCM. Suppressed notifications are hidden if every delivery ends Skipped.
- Fenced completion deactivates UNREGISTERED only when device ID, owner, token and UpdatedAt still match the send snapshot. It records DeviceRevoke audit in the same transaction. Old lease owners cannot invalidate tokens after another worker takes over. INVALID_ARGUMENT, project/auth errors and SENDER_ID_MISMATCH do not automatically deactivate a token.
- 20261107154659_AddPushNotificationTemplates inserts ORDER_PAID_PUSH (mandatory) and AUCTION_OUTBID_PUSH (optional), with synchronized designer/snapshot. Up contains only seed inserts. Review template ID/code/locale collisions before applying to an existing database; do not delete historical data automatically. The existing API startup auto-migration behavior still applies if you start the updated API.
- Raw token/access-token values and provider error bodies are excluded from responses/log metadata. FCM accepted message name can appear in delivery log metadata.

## Verified results and limits
Release build: 0 errors, 0 warnings. Full suite: 131 passed, 0 failed/skipped. Tests use isolated PostgreSQL, fake OAuth and fake FCM HTTP transports. They cover HTTP v1 URI/auth/payload, Retry-After delta/date, invalid configuration, temporary OAuth errors, quota 60s/120s backoff, fanout/replay/mandatory/cap/no-device paths, eight concurrent dispatches and workers, per-device retry without resending successes, token reassignment/refresh/revocation and stale-owner invalidation fencing. Existing Email/InApp/SMTP regressions remain passing; model/snapshot agreement verified.

No actual Google credential lookup, FCM send, real-device receipt or project database migration was performed. Real-device validation needs your Firebase environment and client token lifecycle setup. At-least-once limitations remain: an acceptance followed by a crash before result commit may duplicate the notification. Stable deliveryId in data can support client-side duplicate suppression; it is not a provider deduplication guarantee.

Final GitNexus aggregate: CRITICAL (34 tracked files, 93 symbols, 20 processes), including all earlier uncommitted work. New untracked feature files were reviewed manually; tracked-diff counts are not the whole implementation inventory. No commit/deployment performed.
