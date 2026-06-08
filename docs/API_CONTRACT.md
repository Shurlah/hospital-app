# API Contract

Core endpoints implemented in the backend prototype:

- `POST /api/auth/login`
- `POST /api/auth/refresh-token`
- `POST /api/auth/logout`
- `POST /api/users`, `GET /api/users`, `GET /api/users/{id}`, `PUT /api/users/{id}`, `POST /api/users/{id}/disable`
- `POST /api/facilities`, `GET /api/facilities`, `GET /api/facilities/{id}`, `PUT /api/facilities/{id}`
- `POST /api/children`, `GET /api/children`, `GET /api/children/{id}`, `GET /api/children/search`, `GET /api/children/duplicates`
- `POST /api/immunizations`, `GET /api/immunizations/child/{childId}`, `POST /api/immunizations/{id}/corrections`
- `POST /api/appointments`, `GET /api/appointments`, `GET /api/appointments/upcoming`, `POST /api/appointments/{id}/complete`, `POST /api/appointments/{id}/mark-missed`
- `POST /api/sync/upload`, `GET /api/sync/download`, `GET /api/sync/status`
- `GET /api/reports/*`, `GET /api/notifications/sms`, `GET /api/audit-logs`

Errors use:

```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "One or more validation errors occurred.",
    "details": []
  }
}
```
