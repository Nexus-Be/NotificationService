# Database Email delivery worker
Status: VERIFIED by automated tests, 2026-11-07.

## Contract
No new REST endpoint. Template dispatch persists Pending Email; a new background worker processes those rows. Existing synchronous email and in-memory OTP paths remain compatible. Service interfaces live in Application; SMTP adapter and host loop in Infrastructure. No broker/gRPC/repository.

## Settings and retry meaning
DeliveryWorker: Enabled=true, MaxAttempts=3, BaseDelaySeconds=10, PollIntervalSeconds=5, AttemptTimeoutSeconds=30, LeaseSeconds=120. NOTIFICATION_RETRY_LIMIT overrides MaxAttempts and means TOTAL provider submissions (not 3 retries after the first). Exponential retry delays: base * 2^(attempt-1). Limit settings to sensible bounds and require lease > timeout + 10 seconds. Tests use an injectable TimeProvider, not real sleeps.

## Persistence and concurrency
Reuse RowVersion, LastAttemptAt and NextRetryAt; no schema change. Eligible Email rows are not deleted, have a visible notification and enabled channel, and are Pending due now or Sending with expired lease. Channel disabled pauses existing pending work; new dispatches already record disabled-channel requests as Skipped. Previously accepted mandatory/preference decisions remain frozen at dispatch time.
Claim by conditional DB UPDATE on eligible state and expected RowVersion, increment attempt and RowVersion; log Dispatching in the same short transaction. Send outside transaction. Update final state only if that owner still holds the Sending RowVersion. Final result/log and parent status are committed together. Both claim and completion lock the parent before touching delivery rows, avoiding FK/row-lock inversion; refresh the lease timestamp after acquiring that lock. Lock the parent notification while aggregating delivery states to avoid inconsistent multichannel status. Host cancellation leaves claim recoverable; a later worker can reclaim after the lease. At the attempt limit, an expired lease terminates Failed without sending again.

## Results and failure classification
Sent clears retry/error fields. Temporary network/timeout/SMTP 4xx failures become Pending with NextRetryAt until the attempt limit, then Failed. Permanent invalid-address/SMTP 5xx/authentication/configuration failures become Failed immediately. Missing SMTP credentials are SMTP_NOT_CONFIGURED and never simulation success on this worker. Existing direct sender simulation behavior is not modified. Failed is terminal for this implementation; no Dead enum is introduced. Notification remains Pending while work is unfinished; terminal mixtures with Sent+Failed are Partial, all-failed are Failed, all Sent/Skipped are Completed.

## Delivery semantics and logs
Use a stable SMTP Message-Id derived from DeliveryId across retries. Sent means provider accepted the message, not that it reached the user's inbox. An SMTP acceptance followed by a process/DB failure can cause replay and duplicate mail; this is at-least-once processing, not an exactly-once guarantee. A superseded owner's result must not overwrite the new owner's row. Store attempt/error codes in NotificationLog metadata; do not store SMTP passwords or raw email bodies in logs.

## Verification and operation
Build/test Release, including actual PostgreSQL concurrency/recovery/fencing tests and existing regressions. No production provider or project DB used. Retry/failed outcomes are visible through persisted deliveries/logs and existing notification status queries; management/retry endpoints are future scope. Do not start the API against a shared database as part of this task.


## Implementation files
- Notification.Application/Services/Deliveries/: IDeliveryService, DeliveryService, DeliveryWorkerSettings and DeliveryDtos.
- Notification.Application/Interfaces/IEmailDeliverySender.cs: typed provider contract.
- INotificationDbContext exposes inherited EF DatabaseFacade for transactions; Application references EFCore.Relational 8.0.10 directly. No Repository wrapper.
- Notification.Infrastructure/Services/Email/SmtpEmailDeliverySender.cs: strict SMTP adapter, status classification and stable Message-Id; closes locally after DATA acceptance without waiting on QUIT.
- Notification.Infrastructure/BackgroundWorkers/DatabaseEmailWorker.cs: scope-per-iteration polling host; waits after DB errors and exits on cancellation.
- Application/Infrastructure DI registrations, appsettings example files, docker-compose.yml and .env.example contain worker setup/settings. Runtime .env and appsettings were not edited.

## Verified results
Release build: 0 errors, 0 warnings. Full suite: 96 passed, 0 failed, 0 skipped. PostgreSQL tests ran against a disposable local container, including 8 competing workers, expired-lease recovery, old-owner fencing, third-attempt exhaustion without a fourth submission, host cancellation, timeout/backoff, permanent failure, error reset after success, disabled channels, parent-status aggregation across concurrent sibling deliveries and rollback when claim logging fails. Local SMTP protocol fixture verified message submission and stable Message-Id; no external SMTP server or real mailbox was contacted. Tests also verify bad credentials/config do not simulate Sent and invalid retry limits fail startup validation.

No new migration is required; PostgreSQL model/snapshot agreement remains verified. Previous template-dispatch migration is still required on an existing deployment if it has not been applied. Do not confuse schema validation in temporary databases with applying changes to the project database.

## Operations and limitations
Worker is enabled by default. Set DeliveryWorker:Enabled=false (or NOTIFICATION_DELIVERY_WORKER_ENABLED=false in Compose) to pause it. Settings are read at startup; restart to apply changes. The worker only processes non-deleted template-backed Email deliveries, leaving legacy direct/OTP paths untouched. Ensure UTC clocks and settings are consistent across instances. A claim consumes attempt budget even if a process crashes before SMTP submission, keeping recovery bounded by MaxAttempts. SMTP acceptance followed by process/DB failure can still cause duplicate delivery on recovery; stable Message-Id does not guarantee provider-side deduplication.

A failed delivery is terminal; manual resend/retry management endpoints are not part of this change. Broker health and Push remain separate backlog items. No production database or provider was accessed; no commit or deployment performed.

Final graph analysis: GitNexus aggregate CRITICAL (33 tracked changed files, 93 symbols, 20 processes), including previous uncommitted stages. New untracked feature/test files were manually reviewed and are not fully represented by tracked-diff counts. git diff --check passed after line-ending normalization.

## Push support added
The historical DatabaseEmailWorker class now processes both Email and Push through DeliveryService; the class name was retained for compatibility. Push uses per-device destinations, FCM HTTP v1, token snapshot guards and Retry-After-aware backoff. See FCM_PUSH.md. Latest verification: 131 tests passed, 0 failed/skipped; existing Email behavior remains covered.
