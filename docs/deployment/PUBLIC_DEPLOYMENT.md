# Public deployment of Notification Service

Deploy the Docker image from the dedicated release branch. The service listens on port **8007**. The existing GitHub workflow only builds/pushes an image on `main`; it does not deploy a backend and pushing another branch does not produce a hosted URL.

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
4. Check the real HTTPS URL: `/health`, `/health/live`, and especially `/health/ready`. Readiness must report ready, not merely that a process exists.
5. Check `/api/v1/notifications/me` without credentials and with forged identity headers: neither may expose a user's notifications.
6. Check a legitimate User JWT, the dedicated Auction/Wallet credentials, and each generic caller identity through the actual route used by its owner.
7. Verify OTP/business email against a controlled inbox if email delivery is part of release acceptance. SMTP acceptance does not itself prove mailbox receipt. Push requires its own configured Firebase project and actual device receipt.

The deployed URL can only be reported after the hosting platform has created it and these checks have been performed. A GitHub branch URL, Docker image tag or localhost address is not a deployed backend URL.

## Render Free release profile

Render Free blocks outbound SMTP ports465/587. This release uses Production mode, OtpWorker__Enabled=false, DeliveryWorker__Enabled=false, EventLogRetention__Enabled=false and startup migrations=false. Use HTTPS ingress to port8007 and /health/ready as the health-check path. For this profile only, OtpProtection__RequireSharedKeyRing=false is allowed because OTP enqueue/processing is disabled; no existing OTP key-ring bundle needs to be uploaded. Do not enable OTP against a shared DB before restoring the agreed shared key ring and a supported email transport.

Use distinct private Production JWT/backend keys when the local bundle uses a publicly known example key. Existing local callers are unchanged; callers of the new URL must receive the Production configuration separately. The dedicated Auction and Wallet credentials remain scoped to their contracts. No SMTP password is needed for the API/in-app-only release.

Reference: https://render.com/docs/free


## Verification for this release

Release build succeeded. The full test suite passed853/853 (0 failed, 0 skipped) using isolated PostgreSQL. A separate Production Docker smoke test passed9/9: readiness200, anonymous401, forged identity403, actual User JWT200, forged admin elevation403, disabled OTP503, scoped Wallet credentials400 for invalid DTO and401 for invalid key, Swagger200. No production DB upgrade was run.
