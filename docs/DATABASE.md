# EduVerse Multi-Tenant MySQL Foundation (Phase 3)

## Database approach
Start with one MySQL 8 database, schema-owned modules, strict tenant-scoped queries and a tenant-to-database migration path for large institutions. The current tables are `tenants`, `tenant_memberships`, `campuses`, and `schema_migrations`. Institution types are School, College and Mixed.

- Every owned record uses `tenant_id` and an index starting with that column.
- `campuses` has a tenant-unique code and an InnoDB FK to tenants.
- Memberships are scoped to (`tenant_id`, `subject_id`); the API validates active membership on every request.
- Tenant ID comes only from a validated identity claim `tenant_id`, NEVER from request headers, query strings or posted models.
- No privileged cross-tenant queries from a tenant-scoped repository. Service-account/global admin operations require a separate audited workflow in a later phase.
- The `POST /api/v1/institutions/campuses` endpoint requires ADMIN membership for the current tenant; it cannot assign an arbitrary tenant.
- The API does not run DDL on startup and does not silently provision institutions. Migration/tenant onboarding are controlled operational steps.

## Applying baseline migration
Create an empty MySQL 8 database first, then run from repository root:

```bash
mysql --host=127.0.0.1 --port=3306 --user=eduverse_migrator --password eduverse < database/migrations/001_tenant_foundation.sql
```

The command asks for the password securely. Do not hardcode secrets. Use a separate migration credential with DDL rights; the API credential should have only the minimum DML rights necessary. Record and validate applied schema versions before subsequent releases; never edit previously applied migrations.

## Runtime environment
Set `ConnectionStrings__EduVerse` to a MySqlConnector connection string in environment/secret management (not committed). Set `Authentication__Authority` and `Authentication__Audience` to the trusted OpenID Connect issuer; Phase 4 implements issuer provisioning/role lifecycle. Until a trusted issuer is configured, tenant endpoints reject unauthenticated requests.

## API endpoints
- GET `/api/v1/institutions/current` — current accessible tenant.
- GET `/api/v1/institutions/campuses` — tenant-scoped campus list.
- POST `/api/v1/institutions/campuses` — adds campus to current tenant for ADMIN membership.

No MVC/Razor project references the Infrastructure layer. Module business rules and validation remain in Application services. An authenticated principal without membership gets 403. Missing authentication gets 401.

## Scale and future-hardening
MySqlConnector pools connections; use query plans for high-cardinality workloads. Shard/dedicated-db tenant routing can be introduced behind repository abstractions. Later phases must add tenant-aware auditing, stronger policy-based permissions, secure provisioning, backup/restore drills, concurrency safeguards, and structured observability.

## Automated CI integration
CI starts a MySQL 8 service, applies `001_tenant_foundation.sql`, and sets `EDUVERSE_TEST_MYSQL` for the integration suite. Tests create synthetic tenants and memberships, and verify isolation, role restrictions and header spoofing rejection.
