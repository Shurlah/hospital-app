# Architecture

This repository contains the backend as a single ASP.NET Core deployable using a modular monolith with vertical feature slices. Each module owns its endpoints, commands/queries, handlers, and validation concerns. Shared infrastructure is limited to CQRS dispatching, EF Core persistence, security, errors, SMS, and sync primitives.

Mobile and web clients integrate with this API from separate projects/repositories.

The sync path is always API-mediated:

```text
SQLite -> SyncQueue -> ASP.NET Core API -> PostgreSQL
PostgreSQL -> ServerChangeLog -> ASP.NET Core API -> SQLite
```

Mobile writes are saved locally first and uploaded later. The backend uses `SyncInbox` with unique `DeviceId + ClientChangeId` to prevent duplicate processing.
