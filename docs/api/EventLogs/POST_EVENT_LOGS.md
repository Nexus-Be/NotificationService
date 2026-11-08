# Event Logs REST contract
Status: VERIFIED — 2026-11-08. Draft saved before code.

## 1. Routes and RBAC
| Method | Route | Role | Success |
|---|---|---|---|
|POST|/api/v1/notifications/event-logs|Service, Admin|201 new / 200 replay|
|GET|/api/v1/admin/notifications/event-logs|Admin, Staff|200 PagedData<EventLogSummaryResponse>|
|GET|/api/v1/admin/notifications/event-logs/{id}|Admin, Staff|200 EventLogDetailResponse|

Existing trusted Gateway identity: require nonempty user ID and explicit roles. SourceService is reported by the producer, not independently source-authenticated. Gateway must control service principals and strip user-supplied identity headers. No new internal key/broker/JWT mechanism. Staff cannot ingest; Service cannot read. Two thin controllers use IEventLogService.

## 2. Request contract
CreateEventLogRequest: required nonempty EventId(Guid?), SourceService<=100 normalized lowercase ASCII slug, EventType<=150 ASCII name letters/digits/._:-, required non-default OccurredAt(DateTimeOffset?). Optional nonempty ActorId/TargetId GUID; optional ActorRole<=50 and TraceId<=100 nonblank/control-free trimmed strings; required JSON-object Payload. Optional Headers map only x-request-id/x-correlation-id/traceparent, case-insensitive normalized keys, values nonblank/control-free<=256. Reject unknown or duplicate normalized keys; never snapshot HTTP Authorization/Cookie headers.

Payload: structured JSON envelope, not byte-exact raw HTTP. Canonical ordinal object-key order, no duplicate keys, depth<=32, canonical UTF-8<=65536 bytes, no NUL or invalid Unicode. Numbers preserve spelling, <=256 chars, finite double range and exponent magnitude<=300. Numeric 1 versus 1.0 is a different envelope. HTTP body limit131072 bytes (413); domain validation400.

POST example: {"eventId":"f11f0e4a-b784-44fd-9d33-dced4c134346","sourceService":"commerce","eventType":"OrderPaid","occurredAt":"2026-11-08T00:00:00Z","traceId":"order-123","headers":{"x-correlation-id":"order-123"},"payload":{"orderId":"NEX-001","total":150000}}

ListEventLogsRequest: page>=1 default1, pageSize1..100 default20; optional exact sourceService(normalized), eventType, actorId/targetId/eventId(nonempty GUID), traceId, from/to inclusive OccurredAt UTC. Reject reversed dates/offset overflow. SQL Count/Skip/Take; newest OccurredAt then Id. Empty result items:[] with pagination.

## 3. Pure responses, privacy and idempotency
EventLogAcceptanceResponse: id,eventId,sourceService,createdAt,isReplay (no payload). Summary: id,eventId(nullable legacy),sourceService,eventType,occurredAt,actorId,actorRole,targetId,traceId,createdAt. PagedData: items + pagination(pageIndex,pageSize,totalCount,totalPages,hasPreviousPage,hasNextPage). Detail: summary,payload(JSON or null legacy),headers(JSON or null legacy). Never expose RequestHash. No enum changes; event type/source/actor role are strings.

Unique SourceService+EventId includes soft-deleted records. Fingerprint covers normalized envelope, UTC microsecond occurredAt, explicit traceId and sorted headers; excludes generated HTTP trace and ingesting caller. Object ordering/whitespace irrelevant, number spelling significant. Same envelope returns original acceptance even if hidden; detail of hidden row404. Conflicting envelope or legacy matching key with no hash409. Shared trusted service principals may replay each other's identical envelopes. Producer owns payload content: no automatic redaction, credentials must not be sent. No payload console logging. Summary excludes payload/headers; full detail Admin/Staff only. This endpoint does not trigger notifications or replace the audit ledger.

## 4. Errors and persistence
400 VAL_INVALID_INPUT: invalid fields/JSON/headers/filters/pagination. 401 UNAUTHORIZED: missing/empty identity. 403 FORBIDDEN: wrong role. 404 EVENT_LOG_NOT_FOUND: missing/hidden detail. 409 EVENT_LOG_CONFLICT: source/event key has different envelope or legacy hashless row. 413 transport body limit. English sentence-case DomainExceptions via global ProblemDetails.

Oversized body413 uses REQUEST_BODY_TOO_LARGE with safe sentence-case detail in global ProblemDetails (existing middleware corrected to preserve Kestrel413 instead of500).

Reuse EventLog table; add nullable TraceId(max100), Headers(jsonb), RequestHash(max64), queryfilter !IsDeleted. Unique(SourceService,EventId) WHERE EventId IS NOT NULL includes deleted rows. Indexes (OccurredAt,Id),(SourceService,OccurredAt),(EventType,OccurredAt),(ActorId,OccurredAt),TraceId. Single SaveChanges insert atomic; unique race re-reads winner. Legacy rows untouched; no notification/audit insert for raw ingest. No retention/partition conversion yet.

Migration preflight on existing database: group EventLogs by SourceService and nonnull EventId, HAVING count(*)>1. Resolve any duplicates through owner review before creating index, never auto-delete historical data. Task applies migrations only to temporary test DB.

## 5. Pre-code plan and verification
Validate/canonicalize -> hash -> unfiltered key lookup -> insert -> unique race replay/conflict. Filter/project/page in SQL; clone JsonElements for detail. Additive migration, no historical rewrite. Tests: HTTP role matrix/Swagger/validation, PostgreSQL paging/UTC/detail/canonicalization/replay/concurrency/different sources/hidden/legacy/rollback/model. Full Release build/tests with isolated PostgreSQL and fake providers. Update status and handoff after verification.

## 6. Verified handoff
- Service: NotificationService; EventLogsController/AdminEventLogsController; Application: IEventLogService/EventLogService, EventLogRequests/Responses/EventEnvelope.
- Entity: EventLog. Migration 20261107175502_AddEventLogIngestion + designer/snapshot. Prefix generated UTC; Up adds3 nullable columns and6 indexes, no rewrite. Existing source values are not normalized; historical key/index uses actual stored values, new requests normalize source names.
- Release build0 errors/warnings; full suite157 pass,0 fail/skip. Includes14 new HTTP/Kestrel/PostgreSQL tests: roles, Swagger, payload/trace, safe summary, UTC/page/filter, canonical replay, changed envelope,8 concurrent calls, conflict races, hidden/legacy, save failure, JSON/header/body limits. Model has no pending changes.
- Global middleware now preserves Kestrel413 as REQUEST_BODY_TOO_LARGE; other mappings unchanged. Old management Swagger test scopes to controller tag instead of entire admin namespace.
- GitNexus EventLog/config/DI UNKNOWN, manually reviewed and exercised through EF/DI/MVC tests; middleware LOW. Test UNKNOWN due framework discovery. Aggregate diff CRITICAL includes earlier uncommitted stages; untracked feature files reviewed manually.
- No project DB migration/live provider/credentials edits/commit/deploy. Owned PostgreSQL test container removed. Producer integration, source principal binding, retention90days and health pending.

Preflight existing DB through the normal reviewed migration process (not run against project DB in this task):

    SELECT "SourceService", "EventId", count(*)
    FROM "EventLogs" WHERE "EventId" IS NOT NULL
    GROUP BY "SourceService", "EventId" HAVING count(*) > 1;

Any result requires owner reconciliation before unique index creation, never automatic deletion. Also review historical casing/whitespace source conventions before producer rollout.

## Retention clarification (2026-11-08)
Opt-in EventLogRetentionWorker physically purges rows older than90days from server CreatedAt, including soft-deleted rows. Idempotency described above applies while the row exists; purge releases source/event key and re-ingestion can return201 with a new ID. OccurredAt is not the expiration timestamp. See docs/architecture/EVENT_LOG_RETENTION.md for batch/cadence/locking and deployment limits. Default disabled, no project database cleanup or migration ran during this task. Monthly partitions are not implemented.
