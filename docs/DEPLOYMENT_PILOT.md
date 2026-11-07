# Phase 19 — Deployment and Pilot Operations

**Status:** implementation of release infrastructure and pilot runbooks, **not** a production deployment. Issue #19 remains open. The public endpoints and the school/college pilot must be verified on actual provisioned infrastructure.

## Architecture and deployment order

EduVerse has four independently published .NET 10 processes: `api`, `portal`, `hrms`, `ams`. The API is the sole owner of all business logic and MySQL access. UI frontends are thin Razor applications and call authenticated REST APIs. The portal's YARP proxy forwards `/ams` and `/hrms` to their respective local processes.

A minimal Linux host may use systemd and Nginx/Caddy as an HTTPS reverse proxy. The enclosed deployment example binds all .NET listeners to `127.0.0.1` ports 5201 (API), 5200 (Portal), 5203 (HRMS) and 5202 (AMS). **Do not expose these ports publicly.** TLS, domain routing, host security updates, SSH key management, MySQL provisioning, key-ring persistence, and alert delivery are infrastructure/operator responsibilities.

The normal order is:
1. Provision a protected MySQL 8 schema, restricted application account, encrypted backups and restore test; take a verified backup before schema changes.
2. Apply reviewed migrations `database/migrations/001_*.sql` through `015_*.sql`, in order, using a **separate** migration identity. The app identity must have DML, not DDL, permissions. Never run migrations on API startup.
3. Configure application, authentication and SSO settings securely on each host.
4. Deploy API; check `/health/live`, `/health/ready` and latest schema migration.
5. Deploy HRMS and AMS modules, then Portal; validate reverse proxy and login.
6. Conduct authorized multi-tenant smoke tests and record pilot acceptance.

## Release artifacts and manual deployment

Run GitHub Actions **EduVerse module release bundles**, `workflow_dispatch`, on `main`. Choose `all` or one module and `staging` or `production` metadata. The workflow builds separate immutable .NET 10 archives plus SHA-256 manifests; it **does not deploy** or access hosts, databases, or production secrets. The existing GitHub `ci.yml` workflow gates PRs with real MySQL tests and module publishability. Promotion to production requires human review and an external environment approval process.

Download artifacts and their matching `.sha256` files to a private host directory. First install the dedicated `eduverse` service account and the provided `ops/systemd/*.service` units, the .NET 10 ASP.NET Core runtime, and the required env files. Place the deployment script under `/opt/eduverse-ops/deploy`. Run:
```bash
sudo bash /opt/eduverse-ops/deploy/release-module.sh api /secure/incoming/api-<FULL_40_CHAR_SHA>.tar.gz
sudo bash /opt/eduverse-ops/deploy/release-module.sh hrms /secure/incoming/hrms-<FULL_40_CHAR_SHA>.tar.gz
sudo bash /opt/eduverse-ops/deploy/release-module.sh ams /secure/incoming/ams-<FULL_40_CHAR_SHA>.tar.gz
sudo bash /opt/eduverse-ops/deploy/release-module.sh portal /secure/incoming/portal-<FULL_40_CHAR_SHA>.tar.gz
```
This script verifies checksums and module metadata, rejects unsafe archive entries, deploys immutable release directories and atomically repoints `current`. On restart/readiness failure it restores the prior symlink and restarts the earlier version (if present). It is intentionally operator-run and requires `sudo` and the service account to already exist.

## Runtime secrets and configuration

Do not put actual credentials, OAuth secrets, student/employee data or production database information into code, GitHub Actions artifacts or logs.

Provision **root-owned env files** at `/etc/eduverse/env/{api,portal,hrms,ams}.env`, mode `0600`, and `/etc/eduverse/mysql-backup.cnf`, mode `0600`. Use secrets supplied out of band. Required fields:

| Process | Required config |
| --- | --- |
| API | `ConnectionStrings__EduVerse` (MySQL 8 TLS), `Authentication__Issuer`, `Authentication__Audience`, `Authentication__SigningKey` |
| Portal | `Api__BaseUrl=http://127.0.0.1:5201/`, `Auth__KeyRingPath=/var/lib/eduverse/keyring`, `ReverseProxy__Clusters__ams__Destinations__primary__Address=http://127.0.0.1:5202/`, `ReverseProxy__Clusters__hrms__Destinations__primary__Address=http://127.0.0.1:5203/` |
| HRMS | `Api__BaseUrl=http://127.0.0.1:5201/`, `Auth__KeyRingPath=/var/lib/eduverse/keyring` |
| AMS | Same as HRMS |

The shared SSO key ring **must be mounted persistently and writable by `eduverse`**; never use per-container temporary keys. The current code uses a file-system DataProtection key ring without an explicit at-rest key encryption provider: encrypt the backing volume, protect file permissions and backups, and do not assume it is cryptographically encrypted at the application layer. Configure trusted reverse proxy headers/TLS and HTTPS before exposing the service.

All identity, subscription, role and campus validations stay in API services and query scopes. Never bypass them by giving user-facing UIs SQL connectivity.

## MySQL backups and restore drills

Install `ops/mysql/backup.sh` at `/opt/eduverse-ops/mysql/backup.sh` and the systemd `eduverse-mysql-backup.service` plus `.timer`, adjusting database name and filesystem paths to the chosen environment. Configure `MYSQL_DEFAULTS_FILE`, `BACKUP_DIR` and `DB_NAME`. The option file contains `[client]` with TLS connection details and a dedicated minimal-permission backup account. `mysqldump --single-transaction` is consistent for InnoDB only; coordinate DDL/migrations to prevent inconsistent backups.

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now eduverse-mysql-backup.timer
sudo systemctl start eduverse-mysql-backup.service
sudo bash /opt/eduverse-ops/mysql/verify-backup.sh /srv/eduverse-backups/<backup>.sql.gz
```

Backups are checksum-verified compressed SQL, not encrypted automatically by the scripts. Store them on an encrypted filesystem and replicate **off-host** to private encrypted storage. Set retention, alerts for missed backup jobs, and periodic immutable backup snapshots. Rehearse a restore into a **new empty isolated MySQL schema**, never production, using a separate restore account; validate `schema_migrations`, row counts, tenant scoping and login. Record the restore time and compare to recovery objectives. Do not mark backups production-ready before successful isolated restores.

## Monitoring and incident response

Recommended baseline alerts: `/health/ready` unavailable for >2 minutes, MySQL errors/connections, elevated 5xx/401/403/429 rates, prolonged API response times, disk and memory saturation, stopped module services, backup/checksum/restore failures and `integration_outbox_dead_letters` growth. Outbox receipts indicate only internal processing, **not delivery** to third-party providers.

```bash
sudo systemctl status eduverse-api eduverse-portal eduverse-hrms eduverse-ams
sudo journalctl -u eduverse-api --since "30 minutes ago" --no-pager
API_URL=https://api.example.invalid PORTAL_URL=https://erp.example.invalid \
 HRMS_URL=https://erp.example.invalid AMS_URL=https://erp.example.invalid \
 bash ops/deploy/smoke-test.sh
```

For incidents: identify tenant, affected module and UTC time; check health and request trace IDs; preserve audit logs, revoke compromised sessions if necessary; roll back only the affected **application** release when database schema is backwards compatible. Restore the database only after an approved data-recovery decision, with full backups and reconciliation. Never echo secrets or record live PII in tickets.

## Pilot: one school + one college (no real student data in test environments)

Use separate seeded synthetic tenants/campuses first:
- School: registration, staff invitations, student enrollment, attendance, finance, and HRMS/AMS return before offboarding.
- College: programs, regulations, elective enrollments, prerequisites, exam grade import, SGPA/CGPA and progression.
- Both: RBAC and cross-tenant/campus 404/403 boundaries; no IDOR, cookie/CSRF protection, backup restore, API readiness, login after restart, SSO between Portal/AMS/HRMS, basic keyboard/navigation accessibility.
- Operator training: campus admins, finance, HRMS and asset managers; document onboarding/exit controls, reports, escalation and data privacy.

Capture for each check: synthetic fixture, timestamp, tester, expected and actual, trace or CI link, pass/fail, follow-up owner. Obtain explicit pilot acceptance and production approval before handling live student/guardian records.

**Outstanding for Issue #19:** real cloud/server provisioning; hardened public reverse proxy/TLS; environment secrets; scheduled off-host backup verification and documented restore evidence; live deployments; school/college user training; actual monitoring and alert delivery; production performance/security gate.
