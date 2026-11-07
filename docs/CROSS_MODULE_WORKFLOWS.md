# Phase 17 — Cross-module workflows (in progress)

## Architecture
API-first coordination lives in `EduVerse.Application.Workflows`; the MySQL transaction implementation lives in `EduVerse.Infrastructure.MySql`. The Razor administrator page at `/admin/workflows` invokes the CSRF-protected cookie BFF `/bff/workflows`, which forwards only allowlisted routes with the session bearer token. No database work or business policy belongs in a Razor view.

Migration `015_cross_module_workflows.sql` must be applied after 014. GitHub Actions CI applies 001–015 and uses real MySQL 8.

## Implemented REST operations (tenant administrator, valid JWT, active HRMS subscription)
Every mutating route requires an ASCII `Idempotency-Key` header (1–128 characters; letters, digits, `-`, `_`, `.`). Replays of identical requests return the original operation ID and `ALREADY_APPLIED`. Key reuse with different payload produces 409. Identifiers are tenant/campus scoped and never accept a client tenant identifier.

- `GET /api/v1/workflows/employees/{employeeId}/clearance?campusId={campusId}`: count outstanding allocated AMS assets.
- `POST /api/v1/workflows/employees/bind-identity`: `{campusId,employeeId,userId}`; link an ONBOARDING employee to an existing active tenant member and grant campus access in the same transaction.
- `POST /api/v1/workflows/employees/offboard`: `{campusId,employeeId}`; require ACTIVE/ON_LEAVE, no assigned assets, write HR event, deactivate linked MEMBER membership and revoke sessions/campus access atomically.
- `POST /api/v1/workflows/procurement/submit-finance`: `{campusId,purchaseId}`; only ORDERED purchasing records may enter the finance handoff queue, without pretending to post a ledger entry.

A successful operation atomically persists the operation receipt and a JSON event in the existing `integration_outbox`.

## Thin UI and operational behavior
The page supports identity binding, clearance checks, offboarding confirmation and a pending procurement handoff. All writes use anti-forgery tokens and idempotency keys. The server rechecks clearance transactionally even when the screen has already displayed zero assigned assets.

## Safety limits and follow-up before release
- Offboarding the linked tenant ADMIN is rejected; no privilege escalation or automatic invitation is performed. Identity creation and invitation continue through the Identity API.
- The legacy HRMS lifecycle endpoint no longer accepts EXITED. Offboarding must use the clearance workflow.
- Finance handoff is only a PENDING request; a finance approval, budget reservation, invoice/ledger posting and reconciliation worker are **not yet implemented**.
- The outbox worker atomically creates idempotent internal receipts in `workflow_event_receipts` for allowlisted `workflow.*` messages, then marks them processed. Unsupported events retry up to eight times and are recorded in `integration_outbox_dead_letters`. These receipts are **not** proof of delivery to a third-party provider. External provider delivery, operator retry and dead-letter review remain open.
- Department-specific roles, external message dispatch, operator dead-letter replay/review, finance approvals and complete school/college workflow acceptance tests remain open.
- The PR is a draft. CI passing does not mean the full acceptance criteria of Issue #17 are satisfied.
