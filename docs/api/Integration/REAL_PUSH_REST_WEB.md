# Real Push, REST and browser integration
Status: LOCAL VERIFIED — 2026-11-09;LIVE INTEGRATION PENDING USER INPUTS.

## Objective/context
Complete Notification-owned integration seams while actual source backend,frontend path and Firebase configuration await user input. Read AGENTS/rules/specs,endpoint/API-doc/controller skills;UserNotificationService/interface/controller/DTOs,FCM OAuth provider/sender,worker,device lifecycle,Compose and PG/Kestrel fixtures. REST only. Existing sender already calls FCM HTTP v1;accepted business endpoints persist notification/deliveries/logs/audit before background sends. No duplicate dispatch implementation or new notification type.

## API contract
GET /api/v1/notifications/{id:guid}: any authenticated owner via trusted Gateway identity;200 pure InAppNotificationItemResponse,Cache-Control no-store,AsNoTracking. Owner ID comes from identity,never body/query. Includes active owned Push-only items(IsInboxVisible false),does not auto-mark read,make hidden items visible or change badge. Anonymous401 UNAUTHORIZED,foreign/deleted/missing/Guid.Empty404 NOTIFICATION_NOT_FOUND,malformed GUID route404. No mutation/audit/schema/enum changes;no pagination for singleton. Existing feature-specific detail/read/preference routes remain. Consumer renders text and authorizes actual order/wallet action independently.

## Real FCM deployment
Opt-in docker-compose.fcm.yml merges main Compose,requires FCM_PROJECT_ID and FCM_CREDENTIALS_HOST_PATH,bind mounts service-account JSON read-only at /run/secrets/firebase-service-account.json,sets GOOGLE_APPLICATION_CREDENTIALS accordingly. No private key/token or DB secret checked in. Config example contains placeholders only. Existing ADC supports non-Docker host credentials. No automatic container startup/migration against configured project DB,live sends or credential edits until actual deployment inputs are identified. Push Sent means FCM accepted,not device displayed/read.

## Web/REST adapter
Framework-neutral ES module integration client uses authenticated Gateway fetch(Bearer supplied by current session,never synthesizes x-user-id/roles). Supports pure DTO and Gateway data envelope,device register/rotation/logout revoke,inbox/unread-count via pagination.totalCount,owned detail,read states/preferences. Supports AbortSignal for account-switch cleanup;no token persistence or built-in auth assumption. Firebase permission/getToken/onMessage/service worker belong actual FE lifecycle;documentation describes foreground refresh and background display without duplicate notification display. Module does not fake Firebase/device success or claim actual FE integration. Existing REST business endpoint contracts must be called only after source commit with durable immutable event retries;errors and dedup remain server-authoritative.

## Plan/impact/verification
IUserNotificationService MEDIUM;service/controller UNKNOWN due stale graph,manually reviewed DI/interface readers and API tests. Add partial feature implementation,thin controller action;no shared worker or provider change.
- [x] Contract and plan before code.
- [x] Owner GET,PostgreSQL HTTP ownership/no-auto-read/Push-only/hidden/model tests.
- [x] Opt-in FCM secret mount and framework-neutral FE API client with Node contract tests.
- [x] Release build0errors/warnings;full751 prior regressions plus new tests;scoped diff/GitNexus,docs/checklists.
- [ ] User identifies actual backend/FE/Firebase configuration and test device.
- [ ] Connect actual source service after commit,FE Firebase lifecycle/Gateway routes,configured DB migration and real Push receipt/foreground/background/logout ownership verification.
No project DB migration,live send,external source/FE mutation or deployment has yet occurred. Do not mark live integration complete based on fake-provider tests. Real rollout should first use explicitly selected test users/devices and verify DB delivery/audit/provider acceptance and device receipt separately.


## Live integration seams — 2026-11-09
Added GET /api/v1/notifications/{id:guid},authenticated owner only,200 pure InAppNotificationItemResponse/no-store/no auto-read. Active Push-only detail allowed without adding to bell;anonymous401,foreign/deleted/missing/empty404 NOTIFICATION_NOT_FOUND. No schema/enum/audit mutation. Interface IUserNotificationService,new UserNotificationService.Detail.cs partial and thin NotificationsController action. FCM data.notificationId can resolve to this route for all types without guessing feature-specific paths. Existing read-state ownership and source-authorization checks remain separate.

Added opt-in docker-compose.fcm.yml and placeholder-only .env.fcm.example for read-only server credential bind mount;requires project ID and existing host file,does not create a missing host directory. Added integrations/web/notification-client.mjs:Gateway Bearer auth/pure DTO or envelope/errors,device rotation/revoke,inbox/unread total count,detail/read states/preferences,AbortSignal;no forged identity headers or embedded key. See integrations/README.md for exact deployment,source durable REST retry,FE Firebase/service-worker lifecycle and live evidence checklist. This module has not been installed into the actual FE. No source business service adapter has been applied to an unconfirmed checkout.

Release build0errors/0warnings;759 .NET tests passed,0failed/skipped(751 retained+8 new real PG/Kestrel owner-detail cases),plus8 Node API-client tests passed. Compose merge validated with fake environment/config --quiet;no Firebase authentication or live provider send. No new migration needed. Owned isolated PostgreSQL test container stopped after verification. No project DB startup/migration/deploy,source/FE mutation,Git staging/reset/commit or credential edits performed.

Pre-edit interface MEDIUM,service/controller UNKNOWN manually followed through DI/interface/controller/readers. Aggregate GitNexus CRITICAL143files/27symbols/16processes includes existing externally staged prior steps and stale graph;manually reviewed scoped diff adds only owner GET/interface,not prior read/auth logic. Scoped staged+unstaged whitespace checks passed;index/pre-existing changes preserved. Live source/FE/FCM/database acceptance remains PENDING INPUTS:identify actual backend/FE,matching Firebase project/Web public config/VAPID/server credential path and selected test users/device. Do not mark real Push/device receipt/production persistence complete based on these tests.


## FE integration and in-place Neon upgrade — 2026-11-09

User identified actual FE and selected Neon,then explicitly chose upgrading the existing database while keeping its data. Read-only inspection found legacy string enums/history and14tables. Backed up privately with pg_dump18,restored on isolated PG18,implemented tools/Notification.LegacyUpgrade and verified dry-run/unknown-enum rollback/apply/idempotence. Live transition committed atomically:16enum columns converted,8original rows fingerprint-verified,27current migrations applied with legacy history/tables retained. Existing template content/IDs preserved;reviewed IsMandatory policy changes allowed. No fake device/message/audit seed inserted. EF runtime read/write+rollback succeeded;pending migrations0,original notifications1. Development/Compose target synchronized to Neon with verified TLS;secret files/backup remain ignored and private. Details:tools/Notification.LegacyUpgrade/README.md.

Native FE wiring now includes typed paged REST adapter,session-scoped unread polling,bells/list/detail/read states,Firebase13.2.0 permission/token/foreground/background/logout lifecycle and tests. FE public config placeholders only;no server credentials included. FE14tests/typecheck/production build pass;lint has0errors and1unrelated pre-existing Commerce busy warning. Guest browser login gate verified;worker503 without config is expected. Backend759 tests passed on isolated PostgreSQL18,0failed/skipped;maintenance build0errors/warnings. No live provider sends or source business events performed.

Remaining:Firebase project/server credential path/public Web config+VAPID,test devices;source durable REST adapters and Gateway Real REST forwarding. Running Gateway is port8000/AuthModeEnforce but notification Mock;available checkout contains pre-existing partial Auth/REST edits and17baseline build errors with Notification stub. Gateway code/container config were not altered. FE local proxy port corrected to8000. Live authenticated FE/FCM end-to-end remains pending. Git indices/other working changes preserved;no commit/push or application deployment.



## Verified local REST stack — 2026-11-09

User identified the correct YARP Gateway checkout(port8080). Its existing notifications/admin catch-all routes already forward REST with JWT validation,identity propagation,anti-spoofing and response envelopes;no Gateway C# route/middleware change was necessary. Bootstrapped private runtime config from public routing example and existing UserService JWT issuer/audience/signature without creating/printing keys.58 Gateway tests passed,build0errors/warnings. The old ApiGateway8000 checkout/container is not part of this integration and was not modified.

Started nexus-gateway8080 and nexus-notification-service8007 using docker-compose.integration.yml overlays,bound only to127.0.0.1. A pre-existing stopped Notification container was preserved by renaming it nexus-notification-service-before-v2-20261109,not deleted;private inspection metadata is in the ignored backup folder. Backups/private appsettings excluded from Docker context. Notification uses the upgraded existing Neon target. Unified durable Email/Push delivery worker is disabled by bootstrap unless NOTIFICATION_LIVE_DELIVERIES_ENABLED=true;FCM credentials are not configured. No provider send or domain event was generated. No public/remote deployment or Git commit/push.

Actual HTTP verification:FE same-origin API proxy→YARP8080→Notification8007→Neon succeeded with a120-second synthetic Buyer JWT and a new,non-account subject for read-only checks. Inbox returned an actual paged empty owner result and preferences returned54defaults. Anonymous inbox401,spoofed x-user-id403,Buyer business dispatch403. No test account,FCM token,order/payment event or notification was fabricated/persisted;no actual user login/device receipt asserted. UserService Swagger was fetched through YARP and its identifier/password login contract matches the FE.

Native FE production server is available locally on3107;API_GATEWAY_URL points to8080.14 FE tests,typecheck/build pass;lint0errors/1pre-existing Commerce busy warning. Guest UI login gate was observed. Backend759tests passed on isolated PostgreSQL18;Neon27current migrations/pending0,8original rows/content preserved and EF read/write rollback verified as documented in tools/Notification.LegacyUpgrade/README.md. Standalone API adapter tests8passed earlier and remain reference-only.

Still pending:Firebase Project ID,server service-account path,public Web config/VAPID,selected actual accounts/devices;authenticated browser token registration/foreground/background/click/read/logout verification;actual committed source-domain events and durable source REST hooks. No claim of real Push/device receipt or all-domain integration until those observations exist.

