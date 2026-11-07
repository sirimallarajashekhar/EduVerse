# Phase 17 — Cross-module workflows (in progress)

## Architecture
API-first coordination lives in `EduVerse.Application.Workflows`; the MySQL transaction implementation lives in `EduVerse.Infrastructure.MySql`. Web clients must invoke the REST API (or a CSRF-protected Razor BFF); no database work or policy checks belong in a Razor view.

Migration `015_cross_module_workflows.sql` must be applied after 014. GitHub Actions CI applies 001–015 and uses real MySQL 8.

## Implemented REST operations (tenant administrator, valid JWT, active HRMS subscription)
Every mutating route requires an ASCII `Idempotency-Key` header (1–128 characters; letters, digits, `-`, `_`, `.`). Replays of identical requests return the original operation ID and `ALREADY_APPLIED`. Key reuse with different payload produces 409. Identifiers are tenant/campus scoped and never accept a client tenant identifier.

- `GET /api/v1/workflows/employees/{employeeId}/clearance?campusId={campusId}`: count outstanding allocated AMS assets.
- `POST /api/v1/workflows/employees/bind-identity`: `{campusId,employeeId,userId}`; link an ONBOARDING employee to an existing active tenant member and grant campus access in the same transaction.
- `POST /api/v1/workflows/employees/offboard`: `{campusId,employeeId}`; require ACTIVE/ON_LEAVE, no assigned assets, write HR event, deactivate linked MEMBER membership and revoke sessions/campus access atomically.
- `POST /api/v1/workflows/procurement/submit-finance`: `{campusId,purchaseId}`; only ORDERED purchasing records may enter the finance handoff queue, without pretending to post a ledger entry.

A successful operation atomically persists the operation receipt and a JSON event in the existing `integration_outbox`.

## Safety limits and follow-up before release
- Offboarding the linked tenant ADMIN is rejected; no privilege escalation or automatic invitation is performed. Identity creation and invitation continue through the Identity API.
- The legacy HRMS lifecycle endpoint no longer accepts EXITED. Offboarding must use the clearance workflow.
- Finance handoff is only a PENDING request; a finance approval, budget reservation, invoice/ledger posting and reconciliation worker are **not yet implemented**.
- The existing outbox worker does **not** deliver these new `workflow.*` messages. They are safely persisted but remain pending and retry as unsupported until an explicit consumer/dispatcher is implemented. Do not turn on this integration in production as if it were delivering messages.
- Department-specific roles, web views, tenant-scoped outbox consumer checkpointing, poison message handling, dead-letter review, and end-to-end offboarding tests remain open.
- The PR is a draft. CI passing does not mean the full acceptance criteria of Issue #17 are satisfied.
