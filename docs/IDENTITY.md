# Phase 4 — EduVerse Identity, Security and SSO

## Design and isolation
- API (ASP.NET Core 10) owns authentication, account records, password verification, tenant membership, role permissions, campus scopes, reset tokens, and audit events.
- Razor Portal + AMS + HRMS are **thin same-origin cookie clients**, sharing `.EduVerse.Auth` and a Data Protection key ring. Browser cookies are HTTP-only; the API access token stays in a protected server-issued cookie ticket, never browser localStorage.
- The API verifies HMAC-SHA256 JWT signatures, audience, issuer, lifetime, session revocation and active membership on every authenticated API request.
- Portal gateway YARP routes require authorization. Direct AMS/HRMS routes also require authentication, but modules **must be internal-only** in production.
- Tenant identity is always an authenticated claim. Campus-scoped reads use an allowlist of campus assignments; ADMINS can see all their tenant campuses.
- Role defaults: `ADMIN` has tenant administrative permissions, `MEMBER` only explicitly granted permissions. The current global MEMBER role grant model should evolve to custom roles in a future enhancement.
- Phase 4 exposes an `/api/v1/identity/permissions/check` API for future module API policy enforcement. HRMS and AMS remain placeholders; business APIs must enforce permissions themselves when implemented.
- PostgreSQL/Redis are not required. MySQL 8 migrations 001 and 002 provision all identity tables.

## Required secrets and configuration
Set using environment variables, managed secrets, or a secret store (never commit):
- `ConnectionStrings__EduVerse`: MySQL 8 connection string with least privilege.
- `Authentication__Issuer`: e.g. `EduVerse.Identity`.
- `Authentication__Audience`: e.g. `EduVerse.Api`.
- `Authentication__SigningKey`: **random 32+ byte secret**, consistent across API replicas and rotated operationally.
- `Auth__KeyRingPath`: same persistent, access-controlled Data Protection key directory for Portal, AMS and HRMS on all nodes. In production use a distributed shared key repository and encryption at rest, not local ephemeral disks.
- `Api__BaseUrl`: trusted internal Identity API URL, configured for each web host.
- `PasswordReset__Url`: HTTPS portal URL ending in `/account/reset`.
- `PasswordReset__Smtp__Host`, `PasswordReset__Smtp__Port`, `PasswordReset__Smtp__From`, `PasswordReset__Smtp__Username`, `PasswordReset__Smtp__Password`: SMTP provider with TLS enabled.

A single tenant may be School, College or Mixed. JWTs are tenant-scoped. Switch institutions by signing in with a different tenant slug; never accept tenant IDs from headers.

## Migrate database
Run migration 001, then 002 with a privileged migration identity. Use separate DML credentials for the API.

```bash
mysql --host=127.0.0.1 --user=eduverse_migrator --password eduverse < database/migrations/002_identity_security.sql
```

## Initial administrator provisioning
Use the one-time local/secure CLI only after migrations. Provide these variables from your secret manager or temporary shell environment:

```powershell
$env:EDUVERSE_BOOTSTRAP_MYSQL = "<your secure MySQL connection string>"
$env:EDUVERSE_BOOTSTRAP_TENANT = "demo-school"
$env:EDUVERSE_BOOTSTRAP_NAME = "Demo School"
$env:EDUVERSE_BOOTSTRAP_KIND = "School"
$env:EDUVERSE_BOOTSTRAP_EMAIL = "admin@example.org"
$env:EDUVERSE_BOOTSTRAP_PASSWORD = "<generate a unique strong password>"
dotnet run --project tools/EduVerse.BootstrapAdmin
Remove-Item Env:EDUVERSE_BOOTSTRAP_PASSWORD
```

Use your own real admin email, institution and protected credentials in production; values above are examples only. CLI will refuse duplicates.

## Identity API routes
- POST `/api/v1/identity/login`: tenant slug + email + password -> 20-minute signed token
- POST `/api/v1/identity/logout`: revoke signed-in session
- GET `/api/v1/identity/session`: live session validation
- GET `/api/v1/identity/me`: subject and tenant context
- GET/POST `/api/v1/identity/users`: tenant ADMIN user directory/invites
- POST `/api/v1/identity/permissions`: ADMIN-controlled MEMBER role grants from a whitelist
- POST `/api/v1/identity/campus-access`: ADMIN-controlled campus assignment
- GET `/api/v1/identity/permissions/check`: module backend permission consultation
- POST `/api/v1/identity/password-reset/request`: generic 202; 15-min token sent only through SMTP
- POST `/api/v1/identity/password-reset/confirm`: atomic one-time token consumption and revoke sessions

Portal routes: GET/POST `/account/login`, POST `/account/logout` with CSRF token, GET/POST `/account/password-reset`, GET/POST `/account/reset`.

## Security notes / next hardening
- This is a functional first-party SSO baseline, not a full external OAuth/OIDC identity provider. For federated third-party SSO use a standards-based provider (e.g. Microsoft Entra ID or OpenIddict) rather than treating this JWT endpoint as an OIDC issuer.
- Require TLS externally and between services, trusted reverse-proxy headers, locked-down private module hosts, restrictive CSP, key rotation, outbox notification delivery, shared distributed rate limiting and incident-response procedures before production.
- Credentials are never stored in source, client-side JS or localStorage. SMTP failures return generic reset responses and are logged without tokens. Do not rely on a password reset until SMTP delivery is configured/tested.
- Current browser cookie validates the API session on each authenticated page request. Large deployments should use secure short-lived cache/session validation and centralized observability.
- HTTP-only SameSite=Lax cookies plus anti-forgery validation guard Razor mutations; backend APIs require Bearer tokens, not browser cookies.
