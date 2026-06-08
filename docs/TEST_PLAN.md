# Test Plan

Priority checks:

- Auth login, refresh rotation, and logout.
- RBAC rejection for protected endpoints.
- Facility and user management.
- Offline child registration upload through `/api/sync/upload`.
- Idempotent sync when the same `DeviceId + ClientChangeId` is repeated.
- Append-only immunization records and duplicate dose rejection.
- Download sync after a server version.
- SMS notification scheduling and delivery attempt logging.
- Reports for immunization coverage, missed appointments, SMS, and sync reliability.
