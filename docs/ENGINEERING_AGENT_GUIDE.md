# Engineering Agent Guide

## Project Name

**Secure Offline-First Immunization Information System**

This document instructs the AI coding agent on how to implement the project. The agent must follow this document strictly.

---

## 1. Engineering Mission

Build a functional prototype of a secure offline-first immunization information system for child healthcare in Alimosho LGA.

The system consists of:

1. React Native mobile app for frontline health workers.
2. ASP.NET Core Web API backend.
3. PostgreSQL central database.
4. SQLite local database on mobile.
5. Web-based admin portal.
6. SMS reminder service.

The backend must use:

- Modular Monolith architecture
- Vertical Slice Architecture
- Custom CQRS dispatcher
- PostgreSQL
- Entity Framework Core
- ASP.NET Core Web API
- Render deployment support

The mobile app must use:

- React Native
- SQLite local storage
- Offline-first write model
- SyncQueue/outbox pattern
- Manual sync
- Network reconnect sync
- Best-effort background sync

---

## 2. Hard Rules

The agent must obey these rules:

1. Do not build microservices.
2. Build a Modular Monolith.
3. Do not use MediatR.
4. Implement custom CQRS dispatcher.
5. Organize backend features by vertical slices.
6. Do not organize backend primarily by Controllers/Services/Repositories.
7. Use PostgreSQL for backend database.
8. Use SQLite for mobile offline database.
9. Do not make SQLite communicate directly with PostgreSQL.
10. All sync must go through the ASP.NET Core Web API.
11. Mobile writes must save locally first.
12. Every offline write must create a SyncQueue item.
13. Backend sync upload must be idempotent.
14. Use client-generated UUIDs for offline-created records.
15. Immunization records must be append-only.
16. Sensitive endpoints must require authentication.
17. Role-based access must be enforced.
18. Secrets must not be committed into source code.
19. SMS gateway keys must come from environment variables.
20. Render deployment configuration must be supported.

---

## 3. Repository Structure

Use this repository for the ASP.NET Core backend only. Mobile and web clients may be implemented in separate repositories that consume this API.

```text
immunization-system-api/
  README.md
  docs/
    PRD.md
    ENGINEERING_AGENT_GUIDE.md
    ARCHITECTURE.md
    API_CONTRACT.md
    DATABASE_SCHEMA.md
    SYNC_DESIGN.md
    SECURITY.md
    TEST_PLAN.md

  src/
    ImmunizationSystem.Api/
      Program.cs
      appsettings.json
      appsettings.Development.json

      Modules/
        Auth/
        Users/
        Facilities/
        Devices/
        Children/
        Guardians/
        Vaccines/
        Immunizations/
        Appointments/
        Sync/
        Notifications/
        Reports/
        AuditLogs/

      Shared/
        Cqrs/
        Database/
        Security/
        Errors/
        Auditing/
        Pagination/
        Time/
        Sms/
        Sync/
        Middleware/

  tests/
    ImmunizationSystem.UnitTests/
    ImmunizationSystem.IntegrationTests/
```

---

## 4. Backend Architecture

The backend is one deployable ASP.NET Core Web API application.

Architecture style:

```text
Modular Monolith + Vertical Slice Architecture + Custom CQRS
```

Each feature should be self-contained.

Example:

```text
Modules/
  Children/
    RegisterChild/
      RegisterChildCommand.cs
      RegisterChildHandler.cs
      RegisterChildValidator.cs
      RegisterChildResponse.cs
      RegisterChildEndpoint.cs
```

Do not create a generic `Services` folder full of unrelated service classes.

Do not create a generic `Repositories` layer unless clearly needed.

Prefer direct EF Core usage inside handlers for this prototype.

---

## 5. Backend Technology Choices

Use:

- .NET 8 or later
- ASP.NET Core Web API
- Entity Framework Core
- Npgsql PostgreSQL provider
- FluentValidation
- BCrypt.Net or Argon2 password hashing
- JWT Bearer Authentication
- Swagger/OpenAPI
- Serilog or built-in structured logging

Do not use:

- MediatR
- MassTransit
- RabbitMQ
- Kafka
- Microservice framework
- Complex event sourcing framework

This is a prototype. Keep the architecture strong but not over-engineered.

---

## 6. Custom CQRS Design

Create the following abstractions:

```csharp
public interface ICommand<TResult>
{
}

public interface IQuery<TResult>
{
}

public interface ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

public interface IRequestDispatcher
{
    Task<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default);

    Task<TResult> QueryAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken = default);
}
```

Implement dispatcher using dependency injection:

```csharp
public sealed class RequestDispatcher : IRequestDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public RequestDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        dynamic handler = _serviceProvider.GetRequiredService(handlerType);
        return handler.HandleAsync((dynamic)command, cancellationToken);
    }

    public Task<TResult> QueryAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResult));
        dynamic handler = _serviceProvider.GetRequiredService(handlerType);
        return handler.HandleAsync((dynamic)query, cancellationToken);
    }
}
```

---

## 7. Backend Module List

Implement these modules:

- Auth
- Users
- Facilities
- Devices
- Children
- Guardians
- Vaccines
- Immunizations
- Appointments
- Sync
- Notifications
- Reports
- AuditLogs

---

## 8. Shared Backend Components

Create shared components:

- Shared/Cqrs
- Shared/Database
- Shared/Security
- Shared/Errors
- Shared/Auditing
- Shared/Pagination
- Shared/Time
- Shared/Sms
- Shared/Sync
- Shared/Middleware

---

## 9. Backend Domain Entities

Implement these entities:

- User
- Role
- Facility
- Device
- Child
- Guardian
- Vaccine
- VaccineSchedule
- ImmunizationRecord
- Appointment
- SyncInbox
- ServerChangeLog
- SmsNotification
- SmsDeliveryAttempt
- AuditLog
- RefreshToken

All main entities should use:

- Guid Id
- DateTime CreatedAt
- DateTime? UpdatedAt
- DateTime? DeletedAt where applicable
- long Version where applicable

Use `Guid` instead of integer IDs to support offline record creation.

---

## 10. Database Provider

Use PostgreSQL.

Connection string should come from environment variables:

```text
ConnectionStrings__DefaultConnection
```

Do not hardcode database credentials.

---

## 11. EF Core DbContext

Create:

```text
ApplicationDbContext
```

It should include DbSet properties for all entities.

Example:

```csharp
public DbSet<User> Users => Set<User>();
public DbSet<Role> Roles => Set<Role>();
public DbSet<Facility> Facilities => Set<Facility>();
public DbSet<Child> Children => Set<Child>();
public DbSet<Guardian> Guardians => Set<Guardian>();
public DbSet<ImmunizationRecord> ImmunizationRecords => Set<ImmunizationRecord>();
public DbSet<Appointment> Appointments => Set<Appointment>();
public DbSet<SyncInbox> SyncInbox => Set<SyncInbox>();
public DbSet<ServerChangeLog> ServerChangeLogs => Set<ServerChangeLog>();
public DbSet<SmsNotification> SmsNotifications => Set<SmsNotification>();
public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
```

Add indexes for:

- Children.FacilityId
- Children.GuardianId
- Guardians.PhoneNumber
- Appointments.AppointmentDate
- Appointments.Status
- SyncInbox.ClientChangeId + DeviceId
- ServerChangeLog.ChangeVersion
- SmsNotifications.Status
- AuditLogs.CreatedAt

---

## 12. Backend Feature Implementation Order

### Phase 1: Foundation

1. Create ASP.NET Core Web API project.
2. Configure PostgreSQL.
3. Configure EF Core.
4. Add health check endpoint.
5. Add Swagger.
6. Add global exception middleware.
7. Add response envelope if desired.
8. Add custom CQRS abstractions.
9. Add dependency injection scanning.

### Phase 2: Security

1. Implement User and Role entities.
2. Implement password hashing.
3. Implement JWT access token generation.
4. Implement refresh token rotation.
5. Implement login endpoint.
6. Implement logout endpoint.
7. Implement role-based authorization policies.

### Phase 3: Master Data

1. Facilities module.
2. Vaccine module.
3. Vaccine schedule module.
4. Device registration module.

### Phase 4: Core Healthcare Records

1. Guardians module.
2. Children module.
3. Immunization module.
4. Appointment module.

### Phase 5: Sync

1. Sync upload endpoint.
2. SyncInbox idempotency.
3. ServerChangeLog.
4. Sync download endpoint.
5. Conflict response handling.

### Phase 6: SMS

1. SMS notification entity.
2. SMS template handling.
3. SMS sender abstraction.
4. SMS provider implementation.
5. Background job to process due SMS.
6. SMS delivery status callback endpoint.

### Phase 7: Reports

1. Immunization coverage report.
2. Missed appointment report.
3. SMS delivery report.
4. Sync reliability report.
5. Facility performance report.

---

## 13. API Endpoint Style

Prefer minimal API endpoint classes per vertical slice.

Example:

```csharp
public static class RegisterChildEndpoint
{
    public static IEndpointRouteBuilder MapRegisterChildEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/children", async (
            RegisterChildCommand command,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var result = await dispatcher.SendAsync(command, cancellationToken);
            return Results.Created($"/api/children/{result.Id}", result);
        })
        .RequireAuthorization("HealthWorker");

        return app;
    }
}
```

Each module should expose a mapping extension:

```csharp
public static class ChildrenModule
{
    public static IEndpointRouteBuilder MapChildrenModule(this IEndpointRouteBuilder app)
    {
        app.MapRegisterChildEndpoint();
        app.MapSearchChildrenEndpoint();
        return app;
    }
}
```

Then in Program.cs:

```csharp
app.MapAuthModule();
app.MapUsersModule();
app.MapFacilitiesModule();
app.MapChildrenModule();
app.MapSyncModule();
```

---

## 14. Sync Design

The sync engine is the most important part of the project.

### 14.1 Sync Rule

Mobile app always writes locally first.

Then it syncs through the API.

Correct flow:

```text
SQLite → SyncQueue → ASP.NET Core API → PostgreSQL
PostgreSQL → ServerChangeLog → ASP.NET Core API → SQLite
```

Never attempt direct SQLite-to-PostgreSQL synchronization.

---

## 15. Backend Sync Upload

Endpoint:

```http
POST /api/sync/upload
```

Request model:

```csharp
public sealed record UploadSyncBatchRequest(
    Guid DeviceId,
    Guid FacilityId,
    Guid HealthWorkerId,
    IReadOnlyList<SyncChangeDto> Changes
);

public sealed record SyncChangeDto(
    Guid ClientChangeId,
    string EntityType,
    Guid EntityId,
    string OperationType,
    JsonElement Payload,
    DateTime ClientTimestamp
);
```

Response model:

```csharp
public sealed record UploadSyncBatchResponse(
    Guid SyncBatchId,
    long ServerVersion,
    IReadOnlyList<SyncItemResult> Results
);

public sealed record SyncItemResult(
    Guid ClientChangeId,
    Guid EntityId,
    string Status,
    string Message
);
```

Statuses:

- Accepted
- Rejected
- Conflict
- Duplicate
- Failed

### Upload processing algorithm

```text
For each incoming change:
  1. Validate device.
  2. Validate user.
  3. Validate facility access.
  4. Check SyncInbox for DeviceId + ClientChangeId.
  5. If already processed, return previous result.
  6. Validate payload based on EntityType.
  7. Process change using correct module handler.
  8. Save resulting entity change to PostgreSQL.
  9. Add SyncInbox record.
  10. Add ServerChangeLog record.
  11. Return item-level result.
```

---

## 16. Backend Sync Download

Endpoint:

```http
GET /api/sync/download?sinceVersion=0
```

Response:

```csharp
public sealed record DownloadSyncResponse(
    long ServerVersion,
    IReadOnlyList<ServerChangeDto> Changes
);

public sealed record ServerChangeDto(
    long ChangeVersion,
    string EntityType,
    Guid EntityId,
    string OperationType,
    JsonElement Payload,
    DateTime ServerTimestamp
);
```

Download processing algorithm:

```text
1. Authenticate user.
2. Validate device.
3. Determine user's allowed facility scope.
4. Read ServerChangeLog where ChangeVersion > sinceVersion.
5. Filter changes by facility permissions.
6. Return ordered changes.
7. Include latest server version.
```

---

## 17. ServerChangeLog

Every successful create/update that mobile devices need to know about should write to `ServerChangeLog`.

Required fields:

- ChangeVersion
- EntityType
- EntityId
- OperationType
- PayloadJson
- FacilityId
- CreatedAt
- CreatedByUserId

`ChangeVersion` should be monotonic and ordered.

Implementation options:

- Option A: PostgreSQL identity column
- Option B: Database sequence

Prefer a PostgreSQL sequence or identity column.

---

## 18. SyncInbox

`SyncInbox` prevents duplicate processing.

Required fields:

- Id
- ClientChangeId
- DeviceId
- EntityType
- EntityId
- OperationType
- Status
- ProcessedAt
- ResultMessage
- CreatedAt

Unique index:

```text
DeviceId + ClientChangeId
```

---

## 19. Conflict Rules

Implement these rules:

```text
Child:
  Detect possible duplicate. If duplicate found, mark IsPossibleDuplicate = true.

Guardian:
  Latest valid update wins.

ImmunizationRecord:
  Append-only. Never overwrite. Duplicate dose should be rejected or flagged.

Appointment:
  Latest valid status wins. Invalid status transition rejected.

Vaccine:
  Server wins.

Facility:
  Server wins.
```

---

## 20. Mobile App Architecture

Use React Native.

Suggested libraries:

- React Navigation
- SQLite library or ORM
- React Hook Form
- Zod or Yup validation
- Axios or Fetch API
- NetInfo
- Secure storage library
- Background fetch library if available

If/when the separate mobile client repository is created, structure it by feature:

```text
src/
  features/
    auth/
    children/
    guardians/
    immunizations/
    appointments/
    sync/
    settings/

  database/
    schema.ts
    migrations.ts
    localDb.ts

  services/
    apiClient.ts
    tokenService.ts
    networkService.ts
    syncService.ts

  shared/
    components/
    validation/
    types/
    utils/
```

---

## 21. Mobile Local Database Tables

Create these SQLite tables:

- Children
- Guardians
- Appointments
- ImmunizationRecords
- Vaccines
- VaccineSchedules
- Facilities
- SyncQueue
- SyncState

---

## 22. Mobile SyncQueue Table

```text
Id
ClientChangeId
EntityType
EntityId
OperationType
PayloadJson
Status
RetryCount
CreatedAt
LastAttemptAt
ErrorMessage
```

Statuses:

- Pending
- Syncing
- Synced
- Failed
- Conflict
- Retrying

---

## 23. Mobile SyncState Table

```text
Id
LastPulledServerVersion
LastSuccessfulSyncAt
LastSyncAttemptAt
PendingCount
FailedCount
```

---

## 24. Mobile Write Pattern

Every create/update action must follow this pattern:

```text
1. Validate input locally.
2. Generate UUID if record is new.
3. Save entity to SQLite.
4. Create SyncQueue item.
5. Mark record as PendingSync.
6. If online, attempt sync.
7. If sync fails, keep item pending or failed.
```

Example:

```text
Register child:
  Save Child locally.
  Save Guardian locally if needed.
  Add SyncQueue item for Guardian create.
  Add SyncQueue item for Child create.
```

---

## 25. Mobile Sync Triggers

Implement these triggers:

1. App starts.
2. Network reconnects.
3. User taps Sync Now.
4. After important online action.
5. Best-effort background sync.

Do not rely only on background sync.

---

## 26. Mobile Sync Algorithm

```text
startSync():
  1. If already syncing, exit.
  2. Check network.
  3. Check valid access token.
  4. Refresh token if needed.
  5. Upload pending SyncQueue items in batches.
  6. Process upload response item by item.
  7. Mark accepted items as Synced.
  8. Mark rejected items as Failed.
  9. Mark conflict items as Conflict.
  10. Download server changes since LastPulledServerVersion.
  11. Apply downloaded changes to SQLite.
  12. Update LastPulledServerVersion.
  13. Update LastSuccessfulSyncAt.
```

Batch size:

```text
20 to 50 records
```

Retry strategy:

```text
Use exponential backoff.
Do not retry permanently invalid data endlessly.
```

---

## 27. Mobile UI Requirements

The mobile app must show sync state clearly.

Dashboard should show:

- Online/Offline status
- Last sync time
- Pending sync count
- Failed sync count
- Appointments due today
- Children registered today
- Sync Now button

The Sync Status screen should show:

- Pending records
- Failed records
- Conflict records
- Last successful sync
- Retry failed sync button

---

## 28. Web Admin Portal

Build a web portal for administrators and supervisors.

Recommended technology:

- React
- TypeScript
- Vite or Next.js
- Tailwind CSS

The portal should include:

- Login
- Dashboard
- User management
- Facility management
- Vaccine schedule management
- Children search
- Appointments
- Missed appointments
- Reports
- SMS notifications
- Sync status
- Audit logs
- Duplicate review

If project time is limited, keep the UI simple and focus on functionality.

---

## 29. SMS Notification Engine

Backend should define:

```csharp
public interface ISmsSender
{
    Task<SmsSendResult> SendAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken);
}
```

Provider implementation should use environment variables:

```text
SMS_PROVIDER
SMS_API_KEY
SMS_SENDER_ID
SMS_BASE_URL
```

Do not hardcode provider credentials.

SMS sending should be logged in:

- SmsNotifications
- SmsDeliveryAttempts

SMS should be processed by a background job.

For prototype:

```text
Use ASP.NET Core BackgroundService.
```

If deploying separately on Render:

```text
Create a Render Worker service.
```

---

## 30. Background Job Requirements

Background jobs:

1. Process due SMS reminders.
2. Process missed appointment follow-up SMS.
3. Optionally mark overdue appointments as missed.

Do not put critical mobile sync only in background jobs. Mobile sync is client-driven.

---

## 31. Security Requirements

Implement:

- JWT access token
- Refresh token rotation
- Password hashing
- Role-based authorization
- Facility-level data filtering
- Audit logging
- Device registration
- HTTPS enforcement
- Input validation
- Secure mobile token storage
- Encrypted mobile SQLite

Authorization policies:

- SystemAdminOnly
- LgaOfficialOnly
- FacilitySupervisorOnly
- HealthWorkerOnly
- CanManageUsers
- CanManageFacilities
- CanViewReports
- CanRecordImmunization
- CanSyncDevice

---

## 32. Render Deployment Requirements

Backend must be deployable on Render.

Add:

- Dockerfile or Render build settings
- Environment variable documentation
- Database migration instructions
- Health check endpoint

Environment variables:

```text
ASPNETCORE_ENVIRONMENT
ConnectionStrings__DefaultConnection
Jwt__Issuer
Jwt__Audience
Jwt__SigningKey
Jwt__AccessTokenMinutes
Jwt__RefreshTokenDays
SMS_PROVIDER
SMS_API_KEY
SMS_SENDER_ID
SMS_BASE_URL
```

Health endpoint:

```http
GET /health
```

---

## 33. Testing Requirements

Create tests for:

- Auth
- RBAC
- Child registration
- Immunization recording
- Appointment creation
- Sync upload
- Sync idempotency
- Sync download
- SMS scheduling
- SMS sending abstraction
- Report queries

Important sync tests:

1. Same ClientChangeId submitted twice should not create duplicate record.
2. Offline child record should sync successfully.
3. Failed sync item should remain retryable.
4. Server changes after version X should be returned.
5. Duplicate child should be flagged.
6. Immunization record should be append-only.

---

## 34. Code Quality Requirements

All code should follow these rules:

- Use clear names.
- Avoid large classes.
- Keep vertical slices focused.
- Do not mix unrelated features.
- Validate input.
- Return meaningful errors.
- Use cancellation tokens.
- Avoid hardcoded secrets.
- Use async database calls.
- Add indexes for query-heavy fields.
- Use pagination for list endpoints.
- Use structured logging.

---

## 35. Error Handling

Use centralized error handling middleware.

Return consistent API errors:

```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "One or more validation errors occurred.",
    "details": []
  }
}
```

Common error codes:

- VALIDATION_ERROR
- UNAUTHORIZED
- FORBIDDEN
- NOT_FOUND
- CONFLICT
- DUPLICATE_RECORD
- SYNC_CONFLICT
- INVALID_OPERATION
- INTERNAL_SERVER_ERROR

---

## 36. Pagination

All list endpoints should support pagination:

```http
GET /api/children?page=1&pageSize=20
```

Response:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 100,
  "totalPages": 5
}
```

---

## 37. Audit Logging Rules

Audit these actions:

- Login success
- Login failure
- Logout
- User created
- User updated
- User disabled
- Role changed
- Facility created
- Facility updated
- Child registered
- Child updated
- Guardian updated
- Immunization recorded
- Appointment created
- Appointment completed
- Appointment missed
- Sync batch uploaded
- SMS sent
- SMS failed

---

## 38. Implementation Prompt for the Coding Agent

Use this prompt when starting implementation:

```text
You are building a secure offline-first immunization information system.

Follow the documents in /docs strictly.

Backend:
- ASP.NET Core Web API
- Modular Monolith
- Vertical Slice Architecture
- Custom CQRS dispatcher
- PostgreSQL
- EF Core
- JWT auth
- RBAC
- Sync upload/download
- SMS notification engine
- Render deployment support

Mobile:
- React Native
- SQLite
- Offline-first writes
- SyncQueue/outbox
- Network reconnect sync
- Manual sync
- Sync status UI

Do not use MediatR.
Do not build microservices.
Do not make SQLite connect directly to PostgreSQL.
Do not hardcode secrets.
Use Guid IDs for syncable entities.
Immunization records must be append-only.
Every mobile write must create a SyncQueue item.
Backend upload sync must be idempotent using DeviceId + ClientChangeId.

Start by creating the backend solution structure, custom CQRS infrastructure, database context, base entities, and Auth module.
```

---

## 39. Suggested Development Sequence for the Agent

The agent should work in this order:

1. Create repository structure.
2. Create backend ASP.NET Core project.
3. Add PostgreSQL and EF Core.
4. Add custom CQRS infrastructure.
5. Add global error handling.
6. Add Auth module.
7. Add Users and Roles.
8. Add Facilities.
9. Add Devices.
10. Add Guardians.
11. Add Children.
12. Add Vaccines and VaccineSchedules.
13. Add Immunizations.
14. Add Appointments.
15. Add SyncInbox and ServerChangeLog.
16. Add Sync upload endpoint.
17. Add Sync download endpoint.
18. Add SMS notification module.
19. Add Reports module.
20. Add AuditLogs.
21. Create mobile app structure.
22. Add SQLite schema.
23. Add local write pattern.
24. Add SyncQueue.
25. Add sync service.
26. Add mobile screens.
27. Create web admin portal.
28. Add tests.
29. Add Render deployment configuration.

---

## 40. Definition of Done

The project is considered functionally complete when:

Backend:

- Auth works.
- RBAC works.
- Facilities can be managed.
- Users can be managed.
- Children can be registered.
- Guardians can be registered.
- Immunizations can be recorded.
- Appointments can be created and updated.
- Sync upload works.
- Sync download works.
- SMS reminders work.
- Reports work.
- Audit logs work.

Mobile:

- Health worker can log in.
- App can save child records offline.
- App can save immunization records offline.
- App creates SyncQueue items.
- App uploads pending changes.
- App downloads server changes.
- App shows sync status.
- App supports manual sync.

Web:

- Admin can log in.
- Admin can manage users and facilities.
- Supervisor can view dashboard.
- Supervisor can view reports.
- Supervisor can view missed appointments.
- Supervisor can view SMS logs.

Security:

- Protected endpoints require authentication.
- Role-based access is enforced.
- Mobile data is protected.
- Tokens are handled securely.
- Audit logs are generated.

Testing:

- Core workflows have tests.
- Sync idempotency is tested.
- Offline-to-online sync is tested.
- RBAC is tested.

---

## 41. Engineering Principle

The system must prioritize correctness and data durability over speed.

The most important rule is:

```text
No health record captured offline should be lost.
```

The second most important rule is:

```text
The same offline event must not be processed twice.
```

The third most important rule is:

```text
Unauthorized users must not access child healthcare records.
```
