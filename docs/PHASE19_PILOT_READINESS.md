# Phase 19 — Pilot readiness: deployment guards and restore evidence

This change supplements [DEPLOYMENT_PILOT.md](DEPLOYMENT_PILOT.md). It is **not** evidence that production or pilot deployment occurred.

## New safety checks

- `ops/deploy/preflight.sh` runs **locally as root** before any release. It inspects availability of .NET 10, MySQL tools, systemd units, the unprivileged `eduverse` account, root-owned environment files, required configuration key presence, persistent shared SSO key-ring permissions, and protected backup-client options. It prints no credential values. It does not create accounts, change permissions or connect to production.
- `ops/deploy/smoke-test.sh` requires an explicit HTTP **2xx** from all five public readiness endpoints, rejects externally supplied cleartext HTTP, and does not follow redirects (including potential HTTPS downgrades), and rejects forged loopback URL authorities. The module release rollback check also requires an HTTP 2xx response. Real authentication and reverse-proxy flows remain separate acceptance criteria.
- `ops/mysql/restore-drill.sh` verifies a checksum and gzip integrity, refuses symlink sources, requires protected credentials and a **new dedicated** `eduverse_restore_...` sandbox schema, imports the backup, then checks migration 015 and table count. It intentionally preserves the sandbox for manual review, never drops databases and never accepts the production database as a target. Use a minimally privileged isolated restore identity with no write privileges on production schemas.
- `ops/deploy/monitor.sh` checks local systemd units, module health endpoints and backup freshness (default max age 30 hours); failed checks return a nonzero exit status for an external alert system. `ops/systemd/eduverse-monitor.service` and `.timer` schedule it every five minutes. This **does not send alerts automatically** and does not certify backup restorability.\n- `ops/tests/ops-safety-tests.sh` uses synthetic fixture backups and mocked commands to test corruption detection, rejection of production-target restores, rejection of public HTTP and rejection of a false-success HTTP 302 health probe. CI executes these checks on pull requests.

## Operator sequence (proposed staging environment)

1. Provision a host with .NET 10 ASP.NET Core runtime, proxy/TLS, `eduverse` user and service files. Configure the files named in the existing Phase 19 deployment document through an approved secret-management channel.
2. Run `sudo bash ops/deploy/preflight.sh`. Resolve all failures before downloading/activating release artifacts.
3. Produce the MySQL backup using `ops/mysql/backup.sh`, and store its checksum separately. Copy to an isolated restore host **only through an encrypted channel**.
4. On the restore host, run `MYSQL_DEFAULTS_FILE=/secure/restore-only.cnf bash ops/mysql/restore-drill.sh /secure/backups/example.sql.gz eduverse_restore_20261107`. Run role, tenant and integrity checks on the restored sandbox, record row counts and measured RTO/RPO, and then remove the sandbox through a **separate approved manual change**.
5. After CI and deployment approval, manually promote an immutable module archive. Validate API, Portal, HRMS and AMS health endpoints using `ops/deploy/smoke-test.sh`, then conduct authorized school/college pilot flows and sign off the checklist.
6. Install `ops/deploy/monitor.sh` at `/opt/eduverse-ops/deploy/monitor.sh` and install/enable `eduverse-monitor.timer`. Observe failed `eduverse-monitor.service` runs through `journalctl` or a separate alerting agent. Track response codes, backup age and isolated restore results. Alerts must be independently configured and tested.

## Pilot acceptance evidence

| Check | Expected | Evidence to attach before issue closure |
| --- | --- | --- |
| Four services | API, Portal, HRMS, AMS running behind TLS | Host inventory, sanitized service status |
| Release rollback | Prior SHA restored after simulated bad release | Redacted deploy logs and Git SHA |
| Backup | At least one nightly off-host encrypted backup | Timestamp, checksum and retention policy |
| Restore drill | Fresh isolated schema, migration 015 present | UTC timestamp, duration, table counts, operator signoff |
| Tenant isolation | School and college records cannot cross tenant/campus | Synthetic test IDs and HTTP result codes |
| Identity | SSO and revocation across Portal/HRMS/AMS | Role test record, no JWTs or passwords |
| Quality | Accessibility, load and security gates | CI/audit links, defects and signoff |
| Training | School/college admins trained | Attendance and anonymized workflow feedback |

**Issue #19 remains open** until actual cloud deployment, backup/restore, monitoring, user training and pilot approval are independently verified. Neither automated CI nor mocked smoke checks prove production readiness.
