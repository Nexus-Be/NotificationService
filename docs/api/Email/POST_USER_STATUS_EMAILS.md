# Account status compatibility email endpoints

Status: LOCAL VERIFIED — 2026-11-10; real SMTP receipt remains blocked by Gmail quota.

## Routes and callers

- POST `/api/v1/notifications/email/user-banned`
- POST `/api/v1/notifications/email/user-unbanned`

EmailController → IEmailNotificationService → strict IEmailDeliverySender. Trusted service/admin only, no-store, body limit 16 KiB. Through Gateway use a valid authorized principal; identity headers are propagated by trusted infrastructure, never supplied by the browser. Anonymous 401; buyer/seller/staff 403. These routes send an email about a decision already committed by the source; they never change account status or revoke sessions.

## Request and response

Ban:

```json
{"email":"seller@example.com","fullName":"Seller Test","reason":"Public decision reason"}
```

Unban:

```json
{"email":"seller@example.com","fullName":"Seller Test","reason":null}
```

Email required/valid/max254. FullName optional/max200; blank gets a neutral greeting. Ban Reason required/nonblank/max1000. Unban Reason optional/max1000; absent/blank omits the reason paragraph. Control characters rejected except newline/tab in reasons. Unknown JSON members rejected. User-supplied name and reason HTML-encoded; no arbitrary HTML, template code, timestamp or URL accepted.

200 returns pure EmailDispatchResponse: `recipient`, `status: "Sent"`, `dispatchedAt` UTC. Sent means SMTP accepted, not mailbox delivery/read. Gmail quota/auth failures return an error, never fake success. These routes use the strict sender; the legacy general-email simulation fallback is not used.

Errors: 400 validation/VAL_INVALID_INPUT; 401 UNAUTHORIZED; 403 FORBIDDEN; 413 oversized body; 503 EMAIL_PROVIDER_UNAVAILABLE if strict sender/SMTP configuration missing; 500 EMAIL_SEND_FAILED if SMTP rejects/fails. Raw provider diagnostics are not returned in the public response. No automatic retry of these direct calls.

## Compatibility boundary

The observed caller sends only email/fullName/reason. These synchronous compatibility routes therefore do not create NotificationRecord/AccountStatusChangeDispatch receipts, do not provide persisted delivery/audit history, do not enforce notification preferences/channel policy, and cannot deduplicate transport replays. A repeated accepted request may send another email. Do not represent them as the durable account-status flow or use their response as proof of the source account state.

For durable database history, policy, account-status revision deduplication and worker retry, use the existing [account-status-changes contract](../AccountStatusChanges/POST_ACCOUNT_STATUS_CHANGES.md) with sourceEventId/userId/statusVersion/changedAt and a public reason. That endpoint, templates, receipts, audit, schema and worker are unchanged by this compatibility patch. The caller should select one dispatch path per decision to avoid duplicate mail.

## Verification and rollout

Kestrel HTTP tests cover the exact caller body, ban/unban wording, optional reason, HTML encoding, permissions, invalid/unknown fields, provider failure/missing configuration, pure response and Swagger. Existing email constructors remain compatible with an optional strict sender dependency appended last. No migrations or enums added.

Local deployment uses the current integration Compose overlay with the shared durable worker disabled. Verify both routes in Swagger and anonymous rejection without sending real decision messages. Provider receipt remains blocked while the configured Gmail sender reports daily sending limit exceeded. Do not disable authorization to test, create fake ban events or repeatedly retry the quota error.

Verified: 22 new endpoint cases; 46 targeted email tests passed. Full Release regression 781 passed, zero failures/skips on isolated PostgreSQL18. Docker build zero warnings/errors. Live Gateway downstream Swagger exposes both routes; anonymous requests via API8007/Gateway8080/FE3107 all401, no business/provider send attempted. Unified durable worker remains disabled; new direct routes do not depend on it.
