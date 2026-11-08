# EduVerse Advanced RBAC — Phase 20

## Overview
Apply `database/migrations/018_advanced_rbac.sql` **after 017**. The migration is additive and retains all existing tenant ADMIN/MEMBER memberships, login sessions and platform operators.

School and college institution administrators can open `/admin/rbac` (via `/admin/users`) and create tenant-local custom roles, replace role permission sets, assign multiple roles to **MEMBER** users, and revoke assignments. All validation, authorization, permissions, auditing and MySQL operations are in the API/application/infrastructure layers. Razor and JavaScript only call allowlisted authenticated REST endpoints through the CSRF-protected BFF.

### Routes
- GET `/api/v1/rbac/catalog` — allowlisted permission keys
- GET `/api/v1/rbac/roles` — list own tenant's roles and permissions (ADMIN)
- POST `/api/v1/rbac/roles` — create custom role (ADMIN)
- PUT `/api/v1/rbac/roles/{code}/permissions` — replace allowlisted grants (ADMIN)
- GET `/api/v1/rbac/assignments` — own-tenant active membership assignments (ADMIN)
- POST `/api/v1/rbac/assignments` — assign custom role to active MEMBER (ADMIN)
- DELETE `/api/v1/rbac/assignments/{userId}/{roleCode}` — revoke (ADMIN)

### Security design
- No default passwords and no anonymous role administration.
- System roles `ADMIN`, `MEMBER` and `PLATFORM_SUPER_ADMIN` cannot be created as custom roles or assigned through this mechanism. Platform operator session and JWT validation remain separate.
- All role IDs, permissions and assignments are scoped to the **validated tenant JWT**; supplied tenant IDs are never trusted.
- Multiple custom roles combine permission grants. Membership is required in addition to custom assignments.
- Existing campus grants (`identity_campus_access`) are required for MEMBER requests specifying a campus. **Only currently granted campuses can be read.**
- Custom grants are evaluated at request time in `MySqlIdentityStore.HasPermissionAsync`. A change to a role or assignment revokes affected tenant sessions in the same MySQL transaction, requiring a fresh login.
- `identity_audit_events` records role creation, permission updates, assignment and revocation.
- The initial permission-enforced business operations for custom roles are **GET /api/v1/hrms/employees** and **GET /api/v1/ams/assets**. Existing tenant ADMIN remains authorized. Other HRMS/AMS writes remain ADMIN-only.

### Boundaries and remaining work
This is an operational RBAC foundation, **not yet a claim that all 20 ERP modules accept delegated roles**. Existing backend services across Academics, Students, Attendance, Exams, Finance, College, Reporting, Learning, and Operations still need action-specific role gates; they must not rely on hidden menu links. Teacher-only assigned-class restrictions, student-own-record and parent-child ownership are additional domain checks and are not implemented in this PR.

The original `identity_role_permissions` MEMBER-wide grant and `tenant_memberships.role_code` are retained for backwards compatibility; a future migration can consolidate after inventory and test coverage. The UI currently manages only custom roles, not school ADMIN grants.

### Validation
CI applies migrations 001-018 to MySQL 8 and runs API integration tests for admin-only role configuration, forbidden cross-tenant assignments, additive permission grants, campus restrictions, write rejection, revocation and portal authentication.
