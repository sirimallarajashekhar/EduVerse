# EduVerse platform approval operations

## Install and run

1. Apply migrations 001-016 sequentially on MySQL 8. For an existing database that has applied 001-015, execute **only** `database/migrations/016_platform_approvals.sql` with a migration-only account.
2. Configure API `ConnectionStrings:EduVerse`, `Authentication:SigningKey` (32+ UTF-8 bytes), `Authentication:Issuer` and `Authentication:Audience` through development user-secrets or production secret management. Do not put database credentials in Git. Use HTTPS API URL in `EduVerse.Web` `Api:BaseUrl`.
3. Provision a first platform operator out of band **after verifying their identity**. For a brand-new deployment, use the interactive bootstrap utility from a secure terminal with a restricted `ConnectionStrings__EduVerse` environment variable:

```powershell
$env:ConnectionStrings__EduVerse = 'Server=localhost;Database=eduverse;User ID=eduverse_bootstrap;Password=<enter-from-secret-store>'
dotnet run --project tools/EduVerse.PlatformBootstrap/EduVerse.PlatformBootstrap.csproj
Remove-Item Env:ConnectionStrings__EduVerse
```

Use a temporary least-privilege account that can insert into `identity_users` and `platform_operators`; revoke this privilege after bootstrap. Password input is hidden and never sent to the browser or logged. Alternatively, if a verified active user already exists, grant their operator membership via the DBA-only SQL below. This feature intentionally has **no public self-enrollment or bootstrap endpoint**. For the existing-user option, the operator must already have an active identity account with a verified password (e.g. an existing institution ADMIN). Run from a restricted DBA session:

```sql
INSERT INTO platform_operators(user_id)
SELECT id FROM identity_users WHERE email_normalized=UPPER('verified.operator@example.org')
  AND is_active=1;
```

If no matching identity user exists, provision one through a controlled DBA/admin process using an ASP.NET Identity PasswordHasher hash; do not insert plaintext passwords or enable a public platform-signup endpoint. Verify the insert affected exactly one operator before granting access. For production, mandate MFA through the identity provider or a dedicated second factor before rollout; the current username/password operator flow **does not yet provide MFA**.

4. Open `https://localhost:61274/platform/login` and sign in with this operator's identity credentials. Open `/platform/registrations`; enter a review reason and approve or reject.

## Provisioning semantics

- Public `POST /api/v1/institution-registration/requests` creates a PENDING request only.
- Operator-only `GET /api/v1/platform-admin/registrations?status=PENDING` lists a bounded queue.
- Operator-only `POST /api/v1/platform-admin/registrations/{id}/approve` with `{"reason":"Verified ..."}` creates tenant, institution profile, main campus, PILOT subscription, ADMIN membership and audit entry **in one InnoDB transaction**. Same registered email maps to an existing active identity account, otherwise an account is created with a random unusable password.
- Operator-only `POST /api/v1/platform-admin/registrations/{id}/reject` with a review reason rejects without provisioning.
- A second review of the same request returns HTTP 409. Duplicate tenant slugs also return HTTP 409 and rollback.
- A newly created ADMIN must use the existing password reset request process to set a credential: `/account/password-reset`. Configure SMTP before provisioning real customers and verify delivery. Notify the administrator through an approved channel; this feature does not automatically dispatch an onboarding invitation.
- Platform operator identity is stored in `platform_operators`; sessions in `platform_sessions`. Operator JWTs use `PLATFORM_SUPER_ADMIN` with an **empty** tenant scope and are revalidated against database session/active operator on every API request. School ADMIN membership does not grant platform privileges. Operator logout revokes the session.
- Review reasons and operator IDs are recorded in `platform_approval_audit` and `institution_registration_requests`.

## Security and production gates

- Migrate with restricted DDL credentials; API uses least-privilege DML.
- Rotate any credentials previously committed to the repository and remove them from Git history using approved incident procedures; removing current files alone is not enough.
- Operator login currently uses the existing password lockout and IP rate limiter; add MFA, notification delivery, request pagination/filters and retention controls before public production release.
- Audit and reconcile registrations, tenant/membership creation, and email account activation. Do not set `status='APPROVED'` with ad hoc SQL: that would bypass the transaction and audit trail.
- Never give a school user the platform operator role to troubleshoot an ordinary school.
- `dotnet test EduVerse.slnx` runs API/portal security tests with `EDUVERSE_TEST_MYSQL` configured. CI applies 016 and executes tests.
