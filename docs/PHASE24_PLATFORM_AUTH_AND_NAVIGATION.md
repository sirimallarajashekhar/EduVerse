# Phase 24 — Platform Super Admin login audit and navigation

## Verified existing authentication design
- Platform login is **separate** from institution login: `GET /platform/login`, `POST /api/v1/platform-auth/login`.
- API authenticates the supplied credentials against `identity_users` AND active `platform_operators`. It issues a 20-minute JWT with role `PLATFORM_SUPER_ADMIN` and `tenant_id=Guid.Empty`.
- Every API request validates the JWT signature, issuer, audience, expiry and live platform session (`platform_sessions`) against MySQL; revoked operators cannot continue. Tenant ADMIN JWTs are not platform tokens.
- Institution login uses `/account/login` and `/api/v1/identity/login`; it requires institution slug and a tenant membership. A tenant administrator cannot approve platform registrations.
- Administrator provisioning: platform operator reviews `institution_registration_requests` and approves/rejects. Approval transaction creates tenant, main campus, subscription and institution-admin membership. Newly provisioned accounts do NOT have a usable default password; activation depends on configured password-reset email.

## New platform capability
`/platform/console` (protected by platform operator cookie + API JWT) includes institution inventory, kind, subscription plan, active membership counts, module catalog, institution users, invitations, deactivation, and user reactivation/approval. Existing `/platform/registrations` remains the institution approval page.

New platform-only REST endpoints:
- `GET /api/v1/platform-control/institutions`
- `GET /api/v1/platform-control/institutions/{tenantId}/modules`
- `GET /api/v1/platform-control/institutions/{tenantId}/users`
- `POST /api/v1/platform-control/institutions/{tenantId}/users`
- `POST /api/v1/platform-control/institutions/{tenantId}/users/{userId}/deactivate`
- `POST /api/v1/platform-control/institutions/{tenantId}/users/{userId}/approve`

These cross-tenant endpoints accept only a live `PLATFORM_SUPER_ADMIN` session, not institution ADMIN. User mutations require explicit target tenant and are recorded in `platform_control_audit` with operator, tenant, target user and action/reason. Deactivating the last active institution ADMIN is disallowed. Tenant sessions for deactivated users are revoked immediately. Reactivation/approval is an explicit, audited action.

## Navigation
- `/account/login`: tenant-specific institution sign-in; links to separate Super Admin login.
- `/platform/login`: separate super-admin sign-in; successful login redirects to `/platform/console`.
- New responsive sidebar: platform users see operator control center/registration approvals; tenant admins see only module routes delivered by `/api/v1/tenant/modules`, with subscription disabled states; institution users see teacher/parent/student entry points, all of which are protected by API authorization.
- The Web project remains thin: MVC/Razor and JS only, same-origin BFF with CSRF protection, REST API owns all authorization and writes.

## Deployment
1. Apply `database/migrations/020_platform_control_audit.sql` after 019.
2. Deploy API and Web from merged main using the same JWT signing secrets for the cluster.
3. Provision operator identity using the existing `EduVerse.PlatformBootstrap` operator bootstrap tool (do not insert a hard-coded password or mint a token outside the API).
4. Configure `PasswordReset:Smtp:Host`, `From`, credentials, and `PasswordReset:Url` (HTTPS) to deliver activation emails. If SMTP fails after account creation, the API returns `activationEmailSent:false` and the operator must arrange password-reset delivery; no secret is returned.
5. Visit `/platform/login`, then `/platform/console`. Verify tenant admins cannot access the operator API.

## Important security boundary
A platform super admin can oversee and approve institutions and directly manage institution user accounts. **The platform token is not a blanket tenant ADMIN token.** Platform-wide execution of every academic/finance/student/HRMS/AMS mutation across every tenant is not enabled by this change. Such functionality requires an explicit, time-bound, audited institution delegation workflow rather than silently bypassing established tenant and campus controls.

## Tests
CI runs ASP.NET Core 10 build, MySQL 8 schema migrations through 020, role separation, operator login and revocation, cross-tenant member invite/deactivation/approval, audit existence and existing API/portal tests.
