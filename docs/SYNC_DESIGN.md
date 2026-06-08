# Sync Design

Mobile creates a durable SyncQueue item for every local create/update. Upload batches contain `DeviceId`, `FacilityId`, `HealthWorkerId`, and item-level changes. The backend stores processed client changes in `SyncInbox`.

Supported initial upload entity changes:

- `Guardian/Create`
- `Child/Create`
- `ImmunizationRecord/Create`
- `Appointment/Create`

Download sync returns ordered `ServerChangeLog` rows after `sinceVersion`.
