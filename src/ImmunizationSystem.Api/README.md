# Immunization System API

Backend API for managing facility immunization workflows, users, devices, children, guardians, vaccines, appointments, offline sync, SMS notifications, exports, reports, and audit trails.

## Current Features

- JWT authentication with refresh-token rotation and logout revocation
- Role-based authorization for administration, reporting, immunization, and device sync workflows
- Child registration with guardian linkage and duplicate-flag detection
- Vaccine catalog and vaccine schedule management
- Immunization recording with duplicate-dose prevention and correction records
- Appointment management with local default appointment time support
- Due-vaccine calculation from child date of birth plus active vaccine schedules
- Appointment generation from due vaccine schedules
- SMS reminder scheduling for upcoming appointments and missed-appointment follow-up
- Twilio provider support plus a logging SMS provider for development
- CSV export for children and report datasets
- Offline sync upload/download tracking with idempotent inbox processing
- Audit logging and report endpoints for operational oversight

## Tech Stack

- .NET 10 minimal APIs
- Entity Framework Core 10
- PostgreSQL via Npgsql
- JWT bearer authentication
- Swagger/OpenAPI documentation
- CQRS-style request dispatching for selected write workflows

## Getting Started

Restore and build the API:

```bash
dotnet restore
dotnet build
```

Run the API in development:

```bash
dotnet run --launch-profile http
```

The development launch profile binds to a dynamic loopback port. Check the `Now listening on:` line in the console for the actual URL.

## Swagger

Swagger UI is enabled in development:

```text
{baseUrl}/swagger
```

The generated Swagger JSON is available at:

```text
{baseUrl}/swagger/v1/swagger.json
```

The built-in OpenAPI endpoint is also mapped in development:

```text
{baseUrl}/openapi/v1.json
```

Use the Swagger `Authorize` button with a JWT access token returned from `POST /api/auth/login`.

## Configuration

Connection strings are read from `ConnectionStrings:DefaultConnection`.

Development configuration is in `appsettings.Development.json`; shared defaults are in `appsettings.json`.

JWT settings are under the `Jwt` section:

- `Issuer`
- `Audience`
- `SigningKey`
- `AccessTokenMinutes`
- `RefreshTokenDays`

SMS behavior is controlled by:

- `SMS_PROVIDER`
- `SMS_SENDER_ID`
- `SMS_BASE_URL`
- `TWILIO_ACCOUNT_SID`
- `TWILIO_AUTH_TOKEN`
- `TWILIO_FROM_PHONE_NUMBER`

CORS behavior is controlled by:

- `Cors:AllowedOrigins`
- `CORS_ALLOWED_ORIGINS`

Automatic migration behavior is controlled by:

- `Database:ApplyMigrationsOnStartup`
- `APPLY_MIGRATIONS_ON_STARTUP`

The default SMS implementation logs outbound messages. When `SMS_PROVIDER=Twilio`, the API uses Twilio and validates provider callbacks with `X-Twilio-Signature`.

## Database

Apply EF Core migrations:

```bash
dotnet ef database update --context ApplicationDbContext
```

If `dotnet ef` is not installed globally, install it locally or use a tool path:

```bash
dotnet tool install dotnet-ef --version 10.0.0 --tool-path /tmp/dotnet-tools
/tmp/dotnet-tools/dotnet-ef database update --context ApplicationDbContext
```

The initial migration is `20260607102352_InitialCreate`.

To apply only pending EF Core migrations automatically at startup, set:

```text
APPLY_MIGRATIONS_ON_STARTUP=true
```

This does not recreate already-applied migrations. EF Core uses the `__EFMigrationsHistory` table and runs only unapplied migrations.

Seeded roles:

- `SystemAdministrator`
- `LgaHealthOfficial`
- `FacilitySupervisor`
- `HealthWorker`
- `Auditor`

## Authentication

`POST /api/auth/login` returns:

- `accessToken`
- `refreshToken`
- `userId`
- `role`
- `facilityId`

Send authenticated requests with:

```text
Authorization: Bearer {accessToken}
```

Refresh token rotation is handled by `POST /api/auth/refresh-token`. Logout revokes a refresh token through `POST /api/auth/logout`.

Refresh tokens are stored hashed in the database and rejected when used, revoked, expired, or tied to an inactive user.

## Authorization Policies

- `SystemAdminOnly`: system administrators only
- `CanManageUsers`: system administrators
- `CanManageFacilities`: system administrators
- `CanViewReports`: system administrators, LGA officials, facility supervisors, auditors
- `CanRecordImmunization`: health workers, facility supervisors, system administrators
- `CanSyncDevice`: health workers, facility supervisors, system administrators

## Endpoint Overview

Health:

- `GET /health`

Auth:

- `POST /api/auth/login`
- `POST /api/auth/refresh-token`
- `POST /api/auth/logout`

Users:

- `POST /api/users`
- `GET /api/users`
- `GET /api/users/{id}`
- `PUT /api/users/{id}`
- `POST /api/users/{id}/disable`

Facilities:

- `POST /api/facilities`
- `GET /api/facilities`
- `GET /api/facilities/{id}`
- `PUT /api/facilities/{id}`

Devices:

- `POST /api/devices/register`
- `POST /api/devices/{id}/approve`

Guardians:

- `POST /api/guardians`
- `GET /api/guardians/{id}`
- `PUT /api/guardians/{id}`

Children:

- `POST /api/children`
- `GET /api/children`
- `GET /api/children/export`
- `GET /api/children/{id}/due-vaccines`
- `POST /api/children/{id}/generate-appointments`
- `GET /api/children/{id}`
- `GET /api/children/search`
- `GET /api/children/duplicates`

Vaccines:

- `POST /api/vaccines`
- `GET /api/vaccines`
- `PUT /api/vaccines/{id}`
- `POST /api/vaccines/{id}/disable`
- `POST /api/vaccines/{id}/schedules`

Immunizations:

- `POST /api/immunizations`
- `GET /api/immunizations/child/{childId}`
- `POST /api/immunizations/{id}/corrections`

Appointments:

- `POST /api/appointments`
- `GET /api/appointments`
- `GET /api/appointments/upcoming`
- `POST /api/appointments/{id}/complete`
- `POST /api/appointments/{id}/mark-missed`

Sync:

- `POST /api/sync/upload`
- `GET /api/sync/download`
- `GET /api/sync/status`

Notifications:

- `GET /api/notifications/sms`
- `POST /api/notifications/sms/send-test`
- `POST /api/notifications/sms/provider-callback`

Reports:

- `GET /api/reports/immunization-coverage`
- `GET /api/reports/immunization-coverage/export`
- `GET /api/reports/missed-appointments`
- `GET /api/reports/missed-appointments/export`
- `GET /api/reports/sms-delivery`
- `GET /api/reports/sms-delivery/export`
- `GET /api/reports/sync-reliability`
- `GET /api/reports/sync-reliability/export`
- `GET /api/reports/facility-performance`
- `GET /api/reports/facility-performance/export`

Audit Logs:

- `GET /api/audit-logs`

## Offline Sync

Devices upload client-side changes with `POST /api/sync/upload`. The sync inbox records each client change by `ClientChangeId` and `DeviceId` to make repeated uploads idempotent.

Supported uploaded create operations:

- `Guardian`
- `Child`
- `ImmunizationRecord`
- `Appointment`

Clients download server changes with `GET /api/sync/download?sinceVersion={version}`. Responses include the latest `serverVersion` and up to 500 ordered changes.

## Reports And Exports

Report datasets are available both as JSON and CSV file downloads:

- `GET /api/reports/immunization-coverage`
- `GET /api/reports/immunization-coverage/export`
- `GET /api/reports/missed-appointments`
- `GET /api/reports/missed-appointments/export`
- `GET /api/reports/sms-delivery`
- `GET /api/reports/sms-delivery/export`
- `GET /api/reports/sync-reliability`
- `GET /api/reports/sync-reliability/export`
- `GET /api/reports/facility-performance`
- `GET /api/reports/facility-performance/export`

## Children Export

Children can be exported as CSV with guardian details through:

```text
GET /api/children/export
```

Supported optional filters on `CreatedAt`:

- `facilityId={guid}`
- `from=YYYY-MM-DD&to=YYYY-MM-DD`
- `startMonth=YYYY-MM&endMonth=YYYY-MM`
- `startYear=YYYY&endYear=YYYY`

Use only one date filter mode per request. If no date filter is supplied, the endpoint exports all children.

## Child Vaccine Scheduling

The API can derive child due vaccines from `DateOfBirth` plus active vaccine schedules:

```text
GET /api/children/{id}/due-vaccines
```

This returns each active dose with a computed due date and status such as `Completed`, `Scheduled`, `Overdue`, `DueToday`, or `Upcoming`.

Appointments can be generated from those schedule rows through:

```text
POST /api/children/{id}/generate-appointments
```

Optional request body:

```json
{
  "throughDate": "2026-10-31",
  "createdByUserId": "00000000-0000-0000-0000-000000000000"
}
```

If `throughDate` is omitted, the API generates appointments due within the next 84 days. Generated appointments use the standard appointment reminder flow.

This scheduling flow does not directly send reminders from `VaccineSchedule` rows. Reminders are generated only after real `Appointment` records exist.

## SMS Notifications

Appointment creation schedules reminder notifications when the child has a guardian. Missed appointment updates schedule follow-up messages. The background `SmsReminderWorker` sends pending due notifications through the configured `ISmsSender`.

Provider callbacks can update SMS delivery state through:

```text
POST /api/notifications/sms/provider-callback
```

This callback endpoint is anonymous so external SMS providers can call it.

When Twilio is enabled, callback updates are mapped into local statuses such as `Queued`, `Sent`, `Delivered`, and `Failed`.

## Error Handling

The API uses centralized exception handling and problem details. Domain errors use structured codes such as:

- `UNAUTHORIZED`
- `VALIDATION_ERROR`
- `CONFLICT`
- `DUPLICATE_RECORD`

## Project Structure

```text
Modules/              Feature modules and route mappings
Shared/Cqrs/          Request dispatcher abstractions
Shared/Database/      EF Core DbContext and entities
Shared/Errors/        API exception and error middleware
Shared/Security/      JWT, roles, policies, password hashing
Shared/Sms/           SMS sender and reminder worker
Migrations/           EF Core migrations
```
