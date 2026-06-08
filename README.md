# Secure Offline-First Immunization Information System

ASP.NET Core backend prototype for an immunization information system for Alimosho LGA.

## Project

- `src/ImmunizationSystem.Api`: ASP.NET Core Web API using a modular monolith, vertical slices, custom CQRS, PostgreSQL, JWT/RBAC, sync, SMS, reports, and audit logs.
- `tests`: unit and integration test projects.
- `docs`: PRD, engineering agent guide, and architecture/API/security notes.

Mobile and web clients are product requirements, but they are not included in this backend repository.

## Backend

Required environment variables for deployment:

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
CORS_ALLOWED_ORIGINS
```

`CORS_ALLOWED_ORIGINS` should be a comma-separated list of browser frontend origins, for example:

```text
https://your-frontend.vercel.app,http://localhost:5173
```

Local commands:

```bash
dotnet restore ImmunizationSystem.slnx
dotnet build ImmunizationSystem.slnx
dotnet test ImmunizationSystem.slnx
dotnet run --project src/ImmunizationSystem.Api
```

The API exposes `GET /health` and OpenAPI in development.
