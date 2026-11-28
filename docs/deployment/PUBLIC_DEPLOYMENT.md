# Public deployment of Notification Service

Deploy the Docker image from the dedicated release branch. The service listens on port **8007**. The existing GitHub workflow builds and triggers a Render deploy only on pushes to `dev`; a feature-branch push alone does not update `https://nexus-notification-api.onrender.com`.

## Required runtime configuration

Use the platform's private environment-variable or secret settings. Do not commit the real `.env`, appsettings files, SMTP password, JWT keys or OTP key ring.

| Environment variable | Value |
|---|---|
| ASPNETCORE_ENVIRONMENT | Production |
| ASPNETCORE_URLS | http://+:8007 |
| ConnectionStrings__DefaultConnection | Approved PostgreSQL connection string |
| DatabaseInitialization__ApplyMigrationsOnStartup | false |
| Jwt__Issuer | The existing User JWT issuer |
| Jwt__Audience | The existing User JWT audience |
| Jwt__Signature | The existing User JWT signing key; at least 32 UTF-8 bytes |
| InternalSecurity__ServiceKey | Shared backend key used by trusted header-based callers; at least 32 UTF-8 bytes |
| AuctionNotificationAuth__SigningKey | Existing dedicated Auction notification signing key |
| AuctionNotificationAuth__ServiceUserId | Existing agreed Auction service subject |
| WalletNotificationAuth__ServiceKey | Existing dedicated Wallet notification key |
| OtpProtection__RequireSharedKeyRing | true |
| OtpProtection__SharedKeyRingBase64 | Private shared key-ring bundle used by every instance processing this DB |
| Smtp__Host, Smtp__Port, Smtp__UseSsl | Configured SMTP transport settings |
| Smtp__SenderEmail, Smtp__SenderName | Configured sender |
| Smtp__Username, Smtp__Password | Private SMTP credentials |
| EmailProvider | `Smtp` for local SMTP or `Resend` for HTTPS API on Render Free |
| RESEND_API_KEY, RESEND_FROM_EMAIL, RESEND_FROM_NAME | Resend key and approved sender; never commit the key |
| OtpWorker__Enabled | true normally; false on Render Free (OTP endpoint returns503/OTP_DELIVERY_DISABLED) |
| DeliveryWorker__Enabled | false until the DB queue and delivery configuration are reviewed; enable explicitly for business delivery |
| EventLogRetention__Enabled | false initially |
| Swagger__Enabled | false unless API documentation is intentionally exposed |

When a platform runs the Dockerfile directly, Compose does not translate variables such as `NOTIFICATION_DB_CONNECTION` or `SMTP_PASSWORD`. Set the ASP.NET names above or configure the equivalent mapping on the platform. A healthy local Compose setup alone does not prove the cloud runtime received those settings.

OTP has its own worker. When `OtpWorker__Enabled=false`, it does not claim DB jobs, readiness reports it as intentionally Disabled, and OTP requests are rejected rather than queued. Do not point a candidate container at a shared DB with an unrelated key ring, even when `DeliveryWorker__Enabled=false`. All workers sharing the database must use the agreed code/key-ring configuration. Keep migrations disabled; any upgrade to existing data needs its separate reviewed procedure.

## Public identity boundary

`PublicRequestIdentityMiddleware` runs before header identity extraction in every non-Development environment.

- Forwarded `x-user-*` identity is trusted only with the configured shared backend key in `X-Internal-Service-Key`.
- User JWTs are validated against the configured issuer, audience, HS256 signature, lifetime and nonempty UUID subject. Untrusted forwarded identity headers are removed first, so they cannot override token claims.
- A Gateway that forwards a normal signed User bearer token can continue using that token; it must not rely on unverified identity headers alone.
- Generic service callers using forwarded service/admin identity must provide the shared backend key.
- Scoped Auction JWT and Wallet key endpoints retain their dedicated authentication policies. The public-ingress middleware does not turn those credentials into a generic admin identity.
- Anonymous health requests remain available. Do not publish a Development container or a tunnel to a Development port as a production backend.

## Deployment and verification

1. Select an existing VPS/container platform and provide access through its authenticated session or configured SSH profile. Record the target and branch/commit; do not invent a host, URL or paid plan.
2. Build the Dockerfile at repository root, configure the private runtime values, and map the platform's HTTPS ingress to container port 8007.
3. Keep the database migration flag false. Use an approved DB; a local fixture run is not approval to migrate or reset a shared database.
4. Check the real HTTPS URL: `/health` and especially `/health/ready`. Readiness must report ready, not merely that a process exists.
5. Check `/api/v1/notifications/me` without credentials and with forged identity headers: neither may expose a user's notifications.
6. Check a legitimate User JWT, the dedicated Auction/Wallet credentials, and each generic caller identity through the actual route used by its owner.
7. Verify OTP/business email against a controlled inbox if email delivery is part of release acceptance. SMTP acceptance does not itself prove mailbox receipt. Push requires its own configured Firebase project and actual device receipt.

The deployed URL can only be reported after the hosting platform has created it and these checks have been performed. A GitHub branch URL, Docker image tag or localhost address is not a deployed backend URL.

## Render Free release profile

Render Free blocks outbound SMTP ports465/587. This release uses Production mode, OtpWorker__Enabled=false, DeliveryWorker__Enabled=false, EventLogRetention__Enabled=false and startup migrations=false. Use HTTPS ingress to port8007 and /health/ready as the health-check path. For this profile only, OtpProtection__RequireSharedKeyRing=false is allowed because OTP enqueue/processing is disabled; no existing OTP key-ring bundle needs to be uploaded. Do not enable OTP against a shared DB before restoring the agreed shared key ring and a supported email transport.

Use distinct private Production JWT/backend keys when the local bundle uses a publicly known example key. Existing local callers are unchanged; callers of the new URL must receive the Production configuration separately. The dedicated Auction and Wallet credentials remain scoped to their contracts. No SMTP password is needed for the API/in-app-only release.

Reference: https://render.com/docs/free

### Email-enabled demo profile

The earlier API-only release disabled OTP and delivery workers. For an email demo, first verify a sending domain or use only recipients allowed by the provider's test sender, review existing pending deliveries, and configure `EmailProvider=Resend`, `RESEND_API_KEY`, `RESEND_FROM_EMAIL`, and `RESEND_FROM_NAME` as private Render environment variables. Set `OtpProtection__RequireSharedKeyRing=true` and supply the same private `OtpProtection__SharedKeyRingBase64` used by any process sharing the Notification DB. Only then enable `OtpWorker__Enabled=true` and `DeliveryWorker__Enabled=true`. Keep `DatabaseInitialization__ApplyMigrationsOnStartup=false`.

Render Free can spin down after idle, so queued work may wait until the next inbound request starts the instance. The realtime hub at `/hubs/notifications` is authenticated with a User JWT and is a live hint only; clients must reload `/api/v1/notifications/me` after reconnect. The Gateway and FE integration requirements are in `docs/integrations/NOTIFICATION_REALTIME_HANDOFF.md`.


## Verification for this release

The previous API-only release passed 853/853 tests and its Production smoke checks. For the 2026-11-28 candidate, 916/916 tests passed against a restored PostgreSQL fixture. The candidate Docker image returned readiness 200 with DB Healthy; Swagger contained `wallet-topups`, while anonymous inbox and hub negotiate requests returned 401. These candidate checks do not establish that the new code is live on Render or that email reached an inbox. No production DB migration was run.
