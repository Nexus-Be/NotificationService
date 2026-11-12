# Live Push and REST integration

Status: Notification-owned assets and native FE code wiring implemented;Neon upgraded/verified. Actual Gateway/source/Firebase device receipt remains pending. The ES module below is still a reference adapter;the actual FE uses its existing authenticated TypeScript API client.

## 1. Deployment inputs

Use the selected current Notification build,its PostgreSQL database,FCM project ID,and a server service-account JSON stored outside this repository. The FE needs the matching public Firebase Web configuration and public VAPID key. Private service-account keys belong only on the server. Inspect pending migrations and seed ID/code-locale collisions for the selected database before starting the application:startup currently attempts migrations. Do not start against an unidentified database.

For Docker,set the two variables in `.env.fcm.example` privately,then use:

```powershell
docker compose -f docker-compose.yml -f docker-compose.fcm.yml config --quiet
docker compose -f docker-compose.yml -f docker-compose.fcm.yml up -d --build notification-service
```

The overlay binds the existing host JSON read-only at `/run/secrets/firebase-service-account.json`;a missing host path must fail instead of creating a directory. Base Compose remains usable without Firebase. For a native .NET process,set `FCM_PROJECT_ID` and `GOOGLE_APPLICATION_CREDENTIALS` to the existing host JSON path. Do not paste secret JSON into configuration or the FE. Existing ADC obtains the Firebase messaging OAuth token;the worker uses the existing HTTP v1 sender.

## 2. Identity and network

FE calls the authenticated Gateway using its normal session Bearer token. Gateway must validate that token,strip client-supplied identity headers and forward trusted identity/roles. Keep Notification on the trusted backend network:its current middleware consumes forwarded identity headers;it does not authenticate arbitrary internet callers. Browser CORS and exact route forwarding belong the selected Gateway. No need to expose Notification directly or add broad CORS. Source business endpoints require service/admin identity on the trusted lane. No browser admin/service role or embedded server key.

## 3. FE API module

`web/notification-client.mjs` is a framework-neutral module,not an installed application integration. Import it into the actual FE once its repository/auth/router are identified:

```javascript
import { createNotificationClient } from './notification-client.mjs';
const notifications = createNotificationClient({
  baseUrl: gatewayOrigin + '/api/v1/notifications',
  getAccessToken: () => session.getAccessToken() // bind actual FE session API
});
const sessionRequests = new AbortController();
const page = await notifications.list({ signal: sessionRequests.signal });
const unread = await notifications.unreadCount({ signal: sessionRequests.signal });
// Render page.items as text;use item.id as list key,not deliveryId.
const detail = await notifications.detail(notificationId, { signal: sessionRequests.signal });
await notifications.setRead(detail.id, true, { signal: sessionRequests.signal });
// Refresh inbox and unread count after mutation;Push-only detail does not imply bell visibility.
```

Module supports service pure DTO and Gateway `{success,statusCode,data,errors}` responses,HTTP/ProblemDetails/logical failures,AbortSignal,read/unread,mark-all-read,preferences,device registration/rotation/revocation. Uses `pagination.totalCount` for unread badge,not current page length. Authentication token is obtained for each request;no token persistence or identity header synthesis. On account change/unmount,abort requests,unsubscribe listeners and clear the displayed inbox/badge before initializing the new session.

## 4. Firebase lifecycle to wire into the selected FE

Use the FE's installed Firebase SDK and existing service worker/build pipeline. No dependency version or second worker is forced by this repository.

1. After login,offer an explicit enable-notifications action. From that user gesture,request permission. Denied/unsupported Push must leave REST inbox usable.
2. Initialize the matching public Firebase app and messaging instance. Register/bind a service worker for the FE origin;use the public VAPID key with `getToken` and its `serviceWorkerRegistration`.
3. POST the returned real token through `notifications.registerDevice({token,previousToken})`. Save previous-token lifecycle state in the actual FE's account/device scope;do not send private keys or fabricate tokens. Reconcile token on login/reload/rotation,not only first install.
4. Foreground `onMessage`:verify current account matches `payload.data.userId`,refresh REST inbox/badge,then optionally show a text toast. FCM data includes notificationId/deliveryId/userId;retrieve detail through the common owner GET. Provider acceptance is not read;mark-read only for an explicit UI action.
5. Background notification payloads may already be displayed by Firebase/browser. Do not display a duplicate notification in `onBackgroundMessage`. Wire notification click through the FE's same-origin notification route and authenticated owner GET;do not navigate arbitrary payload URLs or inject HTML. Integrate any click handler with the existing service worker before SDK handling as required by Firebase.
6. Periodic REST refresh/visibility refresh can reconcile bell/read changes between tabs or devices;Push is a refresh hint,not the database source of truth. No WebSocket/broker dependency added.
7. Before removing the authenticated session on logout,revoke the current token through REST,then delete/unsubscribe Firebase token using the FE SDK,unsubscribe foreground handlers,abort old requests and clear UI state. Verify user switch on a shared browser with real devices. Surface revocation failure so it can be retried;do not silently claim success.

Firebase references: [Web setup](https://firebase.google.com/docs/cloud-messaging/web/get-started),[receive messages](https://firebase.google.com/docs/cloud-messaging/web/receive-messages). Actual SDK wiring and browser verification remain pending the FE path/configuration.

## 5. Source service REST integration

The source persists an immutable notification command/event ID with its committed business outcome and retries delivery through a durable job. Do not rely on one fire-and-forget call after commit. Do not put provider calls inside the business transaction. A network timeout after Notification committed is retried with the exact same sourceEventId/body;never mint a new ID for retry. HTTP200 acknowledges durable acceptance/replay,not FCM receipt. Retry bounded network/429/5xx failures;surface validation/authorization/configuration/409 conflicts for correction without rewriting accepted historical events. Inspect actual source transaction/job code before adding this adapter.

Start the first live vertical slice with normal-order payment:

```http
POST /api/v1/notifications/order-payments
Content-Type: application/json

{
  "sourceEventId": "<persisted source event GUID>",
  "orderId": "<source order GUID>",
  "shopId": "<source shop GUID>",
  "buyerUserId": "<actual buyer GUID>",
  "sellerUserId": "<actual seller GUID>",
  "orderNo": "<public order number>",
  "totalAmount": 123000,
  "currency": "VND",
  "paidAt": "<confirmed source UTC timestamp>",
  "sellerAcceptDeadline": "<original source acceptance deadline>"
}
```

This body is illustrative placeholders,not executable fixture/source truth. Use the actual DTO/feature contract for each event;do not send rich order/account/phone/address/bank snapshots. User OTP/password/Seller/account routes,Auction routes,shipment/completion/refund/settlement/cancellation all already have separate contracts in `docs/api/`. The source must supply correct identities,committed state and original clocks;Notification receipts do not prove current business state.

## 6. Live acceptance evidence still required

- Selected source,Gateway and actual FE commit/build identified;matching Firebase project/VAPID/registration token and server credentials configured.
- Additive migrations/seeds reviewed and applied to the selected project database;readiness healthy,worker running.
- Test Buyer/Seller log in through Gateway and register actual device tokens;DB ownership matches authenticated IDs.
- A real source test order is paid/committed;Notification persists its receipt,two logical items,device deliveries,logs and audit exactly once. Retrying source event replays without duplicate bell items.
- Worker records FCM provider message ID;separately observe foreground/background receipt on the selected devices. Verify click→owner detail→explicit read→badge refresh. Confirm foreign owner returns404.
- Logout/token rotation/account switch verified;unregistered device invalidation and transient retry verified without losing inbox. Sent and read remain separate.
- Record real eventId/orderId/notificationIds,delivery statuses and device observations without tokens/keys/PII. Until these are observed,the live integration remains unchecked.
