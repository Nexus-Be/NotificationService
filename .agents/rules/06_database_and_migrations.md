# 06 — DATABASE PERSISTENCE & MIGRATIONS (NOTIFICATION SERVICE)

## 1. Database Isolation
* Dedicated database: `notification_db` on host port `5439`.
* Primary key: 100% `Guid` for entities (`Notifications`, `NotificationPreferences`, `DeviceTokens`).

## 2. Entity Configuration & Soft Delete
* Every entity declares `IsDeleted` and `DeletedAt`. Enforce query filter: `builder.HasQueryFilter(x => !x.IsDeleted)`.
* Index on `UserId` and `CreatedAt` for efficient notification history querying.

## 3. Migration Safety
* No destructive migrations (`DROP TABLE`, `DROP COLUMN`) without explicit authorization.
* Startup safe auto-migration: `db.Database.Migrate()` wrapped in `try-catch` checking `db.Database.IsRelational()`.
