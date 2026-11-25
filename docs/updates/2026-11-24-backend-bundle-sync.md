# Backend bundle synchronization — 24/11/2026

The updated backend bundle is integrated while retaining the already deployed OTP/email, scoped service authentication, Wallet/Catalog intake and public ingress protections.

## Included updates

- Internal audit synchronization source and worker health API.
- The updated Catalog name ProductBannedByReport is accepted and normalized to the existing ProductBannedReport type (integer 26).
- Existing NotificationType integers 0–32 are retained. WithdrawalEvent remains 25 and ProductReportResolved remains 27; no database enum renumbering is needed.
- Withdrawal intake keeps its existing Wallet-only authentication, source-event identity, audit transaction and replay/conflict behavior. The updated Application contract delegates to that same event writer instead of creating another pipeline.
- Auction reserve notifications keep one implementation and one dispatch path in the caller integration.
- Public identity validation and the explicit OTP-disabled response remain available for API-only hosting.

## Runtime and configuration

Private configuration files remain unchanged. Existing containers and the public deployment have not been rebuilt or redeployed by this code synchronization. Review the updated service configuration templates and pending migrations before deploying the new backend bundle. No shared database migration was executed.

Changes are prepared locally on codex/sync-backend-20261124. They have not been committed or pushed.

## Compatibility mapping

| Updated caller name | Stored/API canonical type | Database integer |
|---|---|---|
| ProductBannedByReport | ProductBannedReport | 26 |
| Withdrawal result via withdrawal-events | WithdrawalEvent | 25 |
| ProductReportResolved | ProductReportResolved | 27 |

The original withdrawal endpoint response remains the existing ServiceEventResponse. Do not introduce a second controller for the same route or use the alternative standalone event hashing algorithm against historical events.
