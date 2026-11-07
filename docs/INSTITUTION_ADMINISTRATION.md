# EduVerse Phase 6 — Institution Administration

## What is implemented
- Public `POST /api/v1/institution-registration/requests` accepts a validated **pending** School, College or Mixed registration. Rate limited. It does **not** create a tenant, administrator, subscription or login.
- The existing operator-only `tools/EduVerse.BootstrapAdmin` creates a tenant, initial ADMIN, draft profile and PILOT subscription **atomically**, and approves pending requests matching its institution slug and admin email.
- Authenticated tenant members can query their institution profile and subscription feature availability.
- Active tenant ADMINS can update profiles, mark onboarding READY, manage campus-scoped departments and buildings, update allowlisted settings, inspect subscription plans and view tenant dashboard summaries.
- Existing Phase 3 campus creation and Phase 5 idempotent campus API remain available. Departments/buildings are tied by composite tenant+campus foreign keys.
- `tools/EduVerse.AssignPlan` is operator-only and can assign PILOT or STANDARD in a transaction, recording a subscription audit event. **End users cannot select higher-tier features themselves.**
- Portal pages `/register-institution` and `/admin/institution` contain presentation-only forms; all authorization, validation, persistence, onboarding and feature checks are API-owned.

## REST endpoints
| Method | Route | Access |
|---|---|---|
| POST | /api/v1/institution-registration/requests | Anonymous, rate limited |
| GET | /api/v1/institution-admin/dashboard | Current tenant ADMIN |
| GET/PUT | /api/v1/institution-admin/profile | Member read / ADMIN update |
| POST | /api/v1/institution-admin/onboarding/ready | ADMIN |
| GET/POST | /api/v1/institution-admin/resources/departments | ADMIN |
| GET/POST | /api/v1/institution-admin/resources/buildings | ADMIN |
| GET/PUT | /api/v1/institution-admin/settings | ADMIN |
| GET | /api/v1/institution-admin/plans | ADMIN |
| GET | /api/v1/institution-admin/subscription | ADMIN |
| GET | /api/v1/institution-admin/feature-access/{featureCode} | Member |

The authenticated subject's `tenant_id` is taken only from a validated JWT, never from a query string, route parameter or request body. All owned data operations additionally use a tenant filter and, for resources, a composite tenant/campus FK.

## MySQL schema & deployment
Apply migrations in order: 001, 002, 003, **004_institution_administration.sql**. Use a privileged migration account separate from the application DB account.

```bash
mysql -h 127.0.0.1 -u eduverse_migrator --password eduverse < database/migrations/004_institution_administration.sql
```

New tables: `institution_registration_requests`, `institution_profiles`, `institution_departments`, `institution_buildings`, `institution_settings`, `subscription_plans`, `subscription_plan_features`, `institution_subscriptions`.

## Operator review and provisioning
1. Review registration requests from the MySQL `institution_registration_requests` queue with restricted operator access; verify ownership and institution credentials **outside** the public API.
2. Set `EDUVERSE_BOOTSTRAP_MYSQL`, `EDUVERSE_BOOTSTRAP_TENANT`, `EDUVERSE_BOOTSTRAP_NAME`, `EDUVERSE_BOOTSTRAP_KIND`, `EDUVERSE_BOOTSTRAP_EMAIL`, and `EDUVERSE_BOOTSTRAP_PASSWORD` in a secure operator session. Run `dotnet run --project tools/EduVerse.BootstrapAdmin`. Credentials must never be logged or committed. Bootstrap creates the initial PILOT subscription.
3. An authorized operator can select an existing tenant and set `EDUVERSE_OPERATOR_MYSQL`, `EDUVERSE_OPERATOR_TENANT` (slug), `EDUVERSE_OPERATOR_PLAN` (PILOT or STANDARD), then run `dotnet run --project tools/EduVerse.AssignPlan`. Operator role and audit log are deployment concerns; this tool must never be run in an exposed web worker.

## Subscriptions are feature availability metadata
PILOT grants `core` and `academics`. STANDARD additionally grants `ams`, `hrms` and `reports`.
Subscription `ACTIVE` status and expiration are checked before returning enabled features. Module-specific HRMS/AMS APIs in later phases **must enforce feature access on every request**; merely hiding sidebar navigation does not secure functionality.
Billing, payment-provider integration, trial expirations and invoicing are not included.

## Security and future hardening
- The public registration endpoint is rate limited per IP and does not return internal account/tenant data; add CAPTCHA, verified-email challenges and abuse monitoring before broad release.
- ADMIN permission is rechecked from MySQL, not trusted solely from a token role claim.
- Department/building creation rejects foreign-campus IDs; composite FKs prevent tenant/campus mismatches. No cross-tenant listing.
- Tenant/member data remains separated in SQL, independent from the thin Razor/BFF.
- Existing infrastructure has first-party SSO; secure external identity federation, operator dashboards, registration review UI, subscription billing, advanced plan entitlements, audit detail views and distributed rate limiting are future phases.
- MySQL migration 004 is forward-only, and no production migrations run during API startup.

## CI / tests
GitHub Actions launches MySQL 8, applies migrations 001–004, compiles the full .NET 10 solution, runs service/unit, JWT/MySQL isolation and Razor smoke tests, and verifies publish output. CI contains synthetic test-only records.
