# Event Logs90-day retention
Status: VERIFIED —2026-11-08; architecture draft saved before code.

## Semantics
EventLogRetentionService removes EventLogs where CreatedAt < UTC run start minus90days. CreatedAt is server ingestion time; OccurredAt remains event/filter metadata. Exact cutoff equality kept; timestamps/cutoff normalized PostgreSQL microseconds. Legacy rows use existing CreatedAt. Soft-deleted rows also purged. Physical purge releases SourceService/EventId idempotency key; re-ingestion after expiration can create a new row. No permanent tombstone or permanent dedup promise. All columns/payload/headers/hash on an expired row removed together; AuditLogs and delivery/notification logs untouched.

## Operation
Application EventLogs feature: IEventLogRetentionService.CleanupAsync -> EventLogRetentionResult(Cutoff,DeletedCount,BatchesExecuted,ReachedBatchLimit). PostgreSQL CTE selects expired IDs ordered CreatedAt/Id, LIMIT BatchSize FOR UPDATE SKIP LOCKED; single parameterized DELETE USING per batch. No tracked entity loading/queryfilter exclusion mistake; immutable cutoff throughout run. Each batch commits independently, bounded batch count. Locked rows skipped and eligible next cycle; zero deleted can mean locks, not proof of empty backlog. Concurrent worker instances cannot delete a row twice. Cancellation/failure interrupts loop; committed prior batches remain deleted, unfinished statement rolled back by PostgreSQL.
No endpoint or business service call added. EventLogRetentionWorker resolves a fresh scope per run, runs immediately when enabled, waits configured interval after each run/error, bounds run time with linked cancellation. Shutdown cancellation exits; timeout logs warning and schedules next cycle, database errors logged and retried next interval. Counts/cutoff only in success logs, no payload/credentials.

## Settings and deployment
EventLogRetention section: Enabled=false default; BatchSize500 (1..5000); MaxBatchesPerRun20 (1..100); IntervalSeconds3600 (1..86400); RunTimeoutSeconds120 (1..600). Invalid settings fail startup. RetentionDays fixed constant90, not configurable downward. Service also checks Enabled, so accidental direct invocation while disabled does not hit DB. Example runtime opt-in: EventLogRetention__Enabled=true. Compose uses NOTIFICATION_EVENT_LOG_RETENTION_ENABLED=false by default plus analogous BATCH_SIZE/MAX_BATCHES/INTERVAL_SECONDS/TIMEOUT_SECONDS variables. No actual production config changed in task.
Enable only after normal migration/backup/data-policy review; job permanently deletes eligible data on first cycle. Query-only preview for operators: SELECT count(*) FROM EventLogs WHERE CreatedAt < current UTC time minus interval90days (include hidden rows). Existing create timestamps and retained data should be reviewed, not rewritten by the job. Cleanup asynchronous: rows can remain beyond90days until next successful cycle or longer with backlog/locks/outage. Size batch/cadence to catch up; ReachedBatchLimit signals more cycles may be needed, not an exact backlog metric.

## Schema and scope
Add index(CreatedAt,Id), retain existing filters/uniqueness. Additive migration only; no schema rewrite/delete/partition conversion. Current table non-partitioned: monthly DROP partitions from SRS not implemented in this stage. Exact90-day cutoff within a calendar month would still need row cleanup or finer partition logic. PostgreSQL deletes create dead tuples; disk reuse/reclamation depends on normal autovacuum/maintenance, not instant partition disk release.

## Verification and handoff
Isolated temporary PostgreSQL + host fakes only, no project DB deletion/migration or provider traffic. Tests cover boundary±1microsecond/equality, UTC/server vs producer time, legacy/hidden, unrelated data, bounded batches, locked/concurrent workers, atomic statement/cancellation, cleanup+re-ingestion semantics, migration/model, disabled worker, retries/timeouts/shutdown/scopes/DI. Run full Release build/test; update verified counts/status and migration name. Partition conversion, health, real deployment/monitoring remain backlog. Existing REST API contracts unchanged except documenting finite idempotency after physical expiration.

## Verified result
Release build0 errors/0warnings; full suite180 passed,0failed/skipped. Added23 retention tests (10 PostgreSQL+13 host/settings cases). Actual PostgreSQL tests prove ±1microsecond/equality cutoff, producer time independence, hidden/legacy purge, unrelated data unchanged, concurrency/locked rows, committed prior batches after cancel/failure, statement rollback on trigger error and replay window after purge. Host tests cover disabled no DB/service resolution, validation/binding, scope disposal, error recovery, timeout and shutdown.
Migration20261107181007_AddEventLogRetentionIndex + designer/snapshot; Up only creates IX_EventLogs_CreatedAt_Id. No model drift. New files are EventLogRetentionSettings/Service, IEventLogRetentionService(+result), EventLogRetentionWorker, PostgresEventLogRetentionTests, EventLogRetentionWorkerTests. Existing DI/configuration/examples updated. No API/enum/entity-column changes.
GitNexus pre-edit DI/config UNKNOWN framework edges reviewed by source and DI/PostgreSQL/host tests. Final aggregate CRITICAL includes all earlier uncommitted stages; untracked feature files manually reviewed. No commit/deploy/project DB migration/delete/live provider calls. Temporary owned test container removed after verification. Retention remains disabled in actual runtime until explicitly configured.

Operator preview SQL, through the normal database review process:

    SELECT count(*) FROM "EventLogs"
    WHERE "CreatedAt" < CURRENT_TIMESTAMP - INTERVAL '90 days';

This task did not run the preview or delete against the project DB. Apply migration and confirm policy/backups before enabling. Monthly partition-drop optimization is explicitly pending, not counted as complete.
