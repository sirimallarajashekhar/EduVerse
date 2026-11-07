#!/usr/bin/env bash
# Read-only local health summary. Exit nonzero for an external monitor to alert on.
# BACKUP_DIR=/srv/eduverse-backups BACKUP_MAX_AGE_HOURS=30 bash ops/deploy/monitor.sh
set -Eeuo pipefail
: "${BACKUP_DIR:=/srv/eduverse-backups}"
: "${BACKUP_MAX_AGE_HOURS:=30}"
if [[ "$BACKUP_DIR" != /* ||
      ! "$BACKUP_MAX_AGE_HOURS" =~ ^[0-9]{1,3}$ ||
      "$BACKUP_MAX_AGE_HOURS" -lt 1 ||
      "$BACKUP_MAX_AGE_HOURS" -gt 168 ]]; then
  echo 'Invalid monitor backup directory or backup-age threshold.' >&2; exit 2
fi
failed=0
check() {
  if "$@"; then return 0; else failed=1; return 0; fi
}
check_service() {
  local name="$1"
  if systemctl is-active --quiet "eduverse-$name.service"; then
    echo "PASS module=$name state=active"
  else
    echo "FAIL module=$name state=inactive" >&2
    return 1
  fi
}
check_http() {
  local name="$1" url="$2" code
  code="$(curl --silent --show-error --max-time 5 \
    --output /dev/null --write-out '%{http_code}' "$url")" || return 1
  if [[ "$code" =~ ^2[0-9]{2}$ ]]; then
    echo "PASS endpoint=$name code=$code"
  else
    echo "FAIL endpoint=$name code=$code" >&2
    return 1
  fi
}
for service in api portal hrms ams; do check check_service "$service"; done
check check_http api-ready http://127.0.0.1:5201/health/ready
check check_http portal http://127.0.0.1:5200/
check check_http hrms http://127.0.0.1:5203/hrms/health/live
check check_http ams http://127.0.0.1:5202/ams/health/live

newest=0
if [[ -d "$BACKUP_DIR" ]]; then
  while IFS= read -r -d '' file; do
    if [[ -f "$file.sha256" ]]; then
      modified="$(stat -c '%Y' "$file")"
      if (( modified > newest )); then newest="$modified"; fi
    fi
  done < <(find "$BACKUP_DIR" -maxdepth 1 -type f -name 'eduverse-*.sql.gz' -print0)
fi
now="$(date +%s)"
if (( newest > 0 && newest <= now &&
      now - newest <= BACKUP_MAX_AGE_HOURS * 3600 )); then
  echo "PASS backup=recent age_hours=$(( (now-newest)/3600 ))"
else
  echo 'FAIL backup=missing_or_stale' >&2
  failed=1
fi
if [[ "$failed" -ne 0 ]]; then
  echo 'EduVerse local health check FAILED. Notify the operator through your external alerting system.' >&2
  exit 1
fi
echo 'EduVerse local health check passed. This does not verify backup restorable or external provider delivery.'
