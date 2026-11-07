#!/usr/bin/env bash
# CI-safe checks. No live services or production DB required.
set -Eeuo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
for script in "$root"/ops/deploy/*.sh "$root"/ops/mysql/*.sh; do
    bash -n "$script"
done
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin"
# Invalid database names must be rejected before any mysql invocation.
cat > "$work/bin/mysql" <<'EOF'
#!/usr/bin/env bash
echo 'FAIL: mysql reached by malformed schema' >&2
exit 99
EOF
chmod +x "$work/bin/mysql"
printf '%s\n' '-- MySQL dump 10.13' 'CREATE TABLE test (id INT);' | gzip > "$work/backup.sql.gz"
( cd "$work" && sha256sum backup.sql.gz > backup.sql.gz.sha256 )
if ! bash "$root/ops/mysql/verify-backup.sh" "$work/backup.sql.gz"; then
  echo 'Known-good synthetic backup failed integrity check.' >&2; exit 1
fi
printf 'damage' >> "$work/backup.sql.gz"
if bash "$root/ops/mysql/verify-backup.sh" "$work/backup.sql.gz" >/dev/null 2>&1; then
  echo 'Corrupt backup was accepted.' >&2; exit 1
fi
# Restore must refuse all names that could map to live databases.
if PATH="$work/bin:$PATH" MYSQL_DEFAULTS_FILE="$work/client.cnf" \
  bash "$root/ops/mysql/restore-drill.sh" "$work/backup.sql.gz" "eduverse" >/dev/null 2>&1; then
    echo 'Restore drill accepted production database name.' >&2; exit 1
fi
# Public HTTP is forbidden. No real outgoing connection should be attempted.
if API_URL=http://example.invalid PORTAL_URL=https://example.invalid \
  HRMS_URL=https://example.invalid AMS_URL=https://example.invalid \
  bash "$root/ops/deploy/smoke-test.sh" >/dev/null 2>&1; then
  echo 'Smoke probe accepted an insecure public API URL.' >&2; exit 1
fi
echo 'Operational shell syntax, backup integrity rejection and URL guards passed.'
