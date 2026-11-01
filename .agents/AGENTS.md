# NOTIFICATION SERVICE — AI AGENT MISSION PROTOCOL & RULES

> **Service:** NotificationService (Port 8007, Database `notification_db:5439`)
> **Scoping:** 100% Inward-Only. Never reference outward to root or sibling services.

## Local Governance Rules
Every AI Coding Agent working inside NotificationService must strictly comply with the local rules in `.agents/rules/`:
1. [01 Clean Architecture Layers](rules/01_clean_architecture_layers.md): 4-layer structure, Feature Co-location, No-Repository over EF Core.
2. [02 API Routing & Envelopes](rules/02_api_routing_and_envelopes.md): 100% Plural route convention, pure DTO return, zero ResultFilter.
3. [03 Security Headers & Internal API](rules/03_security_headers_and_internal.md): Public controllers; internal key check temporarily disabled, internal email dispatch.
4. [04 Audit & Dispatch Logging](rules/04_audit_and_event_logging.md): AuditableEntity tracking, notification preference and dispatch trails.
5. [05 Error Handling & Enums](rules/05_error_handling_and_enums.md): 100% English sentence-case messages, SCREAMING_SNAKE_CASE error codes, PascalCase JSON enum serialization.
6. [06 Database & Migrations](rules/06_database_and_migrations.md): Database isolation, PostgreSQL integer enums, soft delete, deterministic seed GUIDs.

## Endpoint Workflow
Before adding or changing an endpoint, follow `.agents/skills/new-feature-endpoint/SKILL.md`.

## Verification Checklist
- `dotnet build Notification.sln -c Release` must succeed with 0 errors.
- `dotnet test Notification.sln -c Release` must pass 100%.
