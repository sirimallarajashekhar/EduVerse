# EduVerse Phase 5 — API Foundation and Scalability

## Contract conventions
- Version all public application endpoints under /api/v1; unknown versions should return 404.
- Use `PageQuery` (`page`, `pageSize`, `sort`, `search`) and `PageResult<T>` with `items`, `page`, `pageSize`, `totalCount`, `totalPages`, `hasNext`.
- Page is 1-based, pageSize is 1–100; unsupported sort fields and overlong search return RFC 7807 validation responses.
- All SQL sort expressions must be selected from a developer-owned allowlist; never accept arbitrary SQL identifiers.
- Public example: `GET /api/v1/platform/modules/paged?page=1&pageSize=10&sort=name&search=ams`.
- Error responses are `application/problem+json` with a correlation `traceId`; unhandled exceptions are logged but not exposed to clients.

## Observability and health
- Structured JSON console logging; propagate `X-Trace-Id` (server-generated) on responses. Do not log authorization headers or request bodies.
- `GET /health/live`: process liveness; does NOT touch MySQL.
- `GET /health/ready`: MySQL connectivity and applied migration `003_api_operations`; returns 503 until ready.
- Operations should monitor error rate, p95/p99 latency, traffic and database pool saturation via centralized telemetry infrastructure (not included in this phase).

## Idempotency — campus command reference implementation
- Existing `POST /api/v1/institutions/campuses` retained unchanged for existing clients.
- New `POST /api/v1/institutions/campuses/idempotent` requires authenticated tenant ADMIN plus `Idempotency-Key` header (ASCII visible 8–128 chars).
- Request is authorized on **every** attempt, even replay. Scope is tenant + subject + operation + key.
- Same key and normalized payload returns the original campus, even after retry; changed payload returns 409.
- The idempotency result, campus row and `campus.created` outbox entry are committed in **one transaction**. Concurrent duplicate callers are serialized by InnoDB unique key locking.
- This is a **reference implementation**, not automatic idempotency for all POST endpoints. Other writes must adopt their own explicit scopes and transactional semantics.
- Old idempotency keys are retained indefinitely in this baseline. Implement governed retention only after establishing retry/replay horizon and data retention policy.

## Background outbox
- Migration 003 adds `integration_outbox` and `api_idempotency`.
- Worker disabled by default; set `BackgroundJobs__OutboxEnabled=true` to enable.
- Worker polls up to 20 events per interval using InnoDB `FOR UPDATE SKIP LOCKED`.
- Only `campus.created` is supported, with a local structured-log handler for architectural demonstration. **No email, webhook, or external message delivery** is implemented.
- Unknown event types retry with delay and eventually stop after 8 attempts, requiring operator investigation.
- Additional integrations will need proper idempotent handlers, monitoring, DLQ/alerting and operational replay tools. Handlers must tolerate at-least-once delivery.

## Migrations
Apply 001, 002, then 003 using a separate privileged migration credential:
```bash
mysql -h 127.0.0.1 -u eduverse_migrator --password eduverse < database/migrations/003_api_operations.sql
```

The API credential should have only required DML grants. Run migrations outside API startup.

## Performance checks
- Automated API tests issue 50 concurrent stateless read calls; this is a concurrency regression smoke test, not a p95 SLA.
- Optional k6 script: `k6 run -e BASE_URL=http://localhost:5201 tools/load/k6-api-smoke.js`. Default 10 virtual users for 30 seconds with example p95 500ms goal.
- Do not run load tests against production or third-party systems without explicit approval; provision a representative isolated test environment.
- For production capacity planning measure with 1, 10, 100+ institutions and realistic traffic, tenant-scoped indexes, distributed rate limits, queue depth and database contention.

## Deployment cautions
- Existing Phase 4 SSO/tenant security stays API-owned. The gateway and modules do not access MySQL directly.
- Do not place signing keys, DB credentials or real student/employee data in source control.
- A complete distributed orchestration and monitoring stack (OpenTelemetry collector, message broker, shared rate limiter, dashboard) remains future work.
