# Security

- JWT access tokens and refresh token rotation are implemented.
- Passwords are hashed using BCrypt.
- Role policies protect admin, report, sync, and immunization workflows.
- Facility scoping is represented in tokens and is available to endpoint handlers.
- Secrets must come from environment variables in deployment.
- SMS credentials are never committed.
- Mobile local data should use SQLCipher-capable SQLite in the separate React Native client implementation.
