# Shared OTP key configuration contract
> **Tác giả:** Hiển Vinh & AI Agent
> Pre-code operational contract, 2026-11-12.

Public REST endpoints, authorization, request/response DTOs and database schema remain unchanged. This changes only the trusted runtime key repository used by the existing OTP protector.

`OtpProtection:SharedKeyRingBase64` contains UTF-8 XML `<keyRing>` with one or more exported ASP.NET Core Data Protection `<key>` elements. The bundle is private master-key material, even when Base64 encoded. `OtpProtection:RequireSharedKeyRing=true` rejects missing bundles at startup. Shared mode uses application name Nexus.Notification.Otp and existing Payload.v1/Fingerprint.v1 purpose strings, disables automatic generation, and never writes/imports keys into a machine-local volume. Replicas given the same bundle can protect/unprotect each other's jobs without sharing a filesystem.

The parser bounds input, prohibits DTD/external entities, validates key IDs/version/dates and the supported built-in descriptor, rejects encrypted-at-rest or custom-deserializer XML requiring machine-specific services, and requires at least one active nonexpired key. It reports only safe configuration errors. Expired keys may be retained for historical decryptability; deployments need an active key to start. Bundle generation/rotation belongs to the owner, not ordinary developers.

Private file layout: `.env` at repository root for Docker; `appsettings.json` and `appsettings.Development.json` under Notification.Api for direct .NET/IDE execution. Docker deliberately excludes JSON secret files and gets its effective values from .env. Original root JSON copies may be retained privately for compatibility. No real secrets in tracked examples, documentation, logs or commits.

All shared-DB Notification consumers require coordinated update; stop incompatible old copies before verification. This feature does not locate/terminate remote workers or claim existing failed OTPs can be retried. Existing file-based mode is retained only when the shared-bundle requirement is explicitly disabled.

## Verification — 2026-11-12
Release build and Docker build succeeded with zero warnings/errors. Full regression:797 passed,0 failed/skipped on isolated PostgreSQL, using two test threads; owned test database removed. Sixteen new cases include independent-provider protection, malformed/unsafe key config and cross-worker processing/replay for all three OTP purposes. The actual private env/JSON files were separately verified to cross-decrypt without shared disk. Runtime readiness200/Ready with DB timeout15, migrations disabled and existing volume preserved. Live FE-proxy password-reset request72258518-d651-4352-a429-c96e76c5c63f reached ProviderAccepted after1attempt; mailbox confirmation is tracked separately. Every shared-DB consumer still needs coordinated rollout of matching code/config. No API/schema migration or source password change.

References for the underlying framework behavior: [Microsoft Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-8.0), [key storage format](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-format).
