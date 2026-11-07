#!/usr/bin/env bash
# Consistent InnoDB backup; secrets in owner-only MYSQL_DEFAULTS_FILE, never CLI arguments.
# MYSQL_DEFAULTS_FILE=/etc/eduverse/mysql-backup.cnf BACKUP_DIR=/srv/eduverse-backups DB_NAME=eduverse bash ops/mysql/backup.sh
set -Eeuo pipefail
umask 077
: "${MYSQL_DEFAULTS_FILE:?Set MYSQL_DEFAULTS_FILE to a protected MySQL client option file}"
: "${BACKUP_DIR:?Set BACKUP_DIR to a secure off-host-backed directory}"
: "${DB_NAME:?Set DB_NAME}"
[[ "$MYSQL_DEFAULTS_FILE" == /* && -f "$MYSQL_DEFAULTS_FILE" ]] || { echo "MYSQL_DEFAULTS_FILE must be an absolute existing path." >&2; exit 2; }
[[ "$BACKUP_DIR" == /* ]] || { echo "BACKUP_DIR must be absolute." >&2; exit 2; }
[[ "$DB_NAME" =~ ^[a-zA-Z][a-zA-Z0-9_]*$ ]] || { echo "Unsafe database name." >&2; exit 2; }
# Disallow group/other permissions on credential files.
perms="$(stat -c '%a' "$MYSQL_DEFAULTS_FILE")"
if (( (8#$perms & 8#077) != 0 )); then
  echo "MySQL credentials file must not grant group/other permissions." >&2; exit 1
fi
mkdir -p "$BACKUP_DIR"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
output="$BACKUP_DIR/${DB_NAME}-${timestamp}.sql.gz"
temporary="$output.partial"
trap 'rm -f -- "$temporary"' EXIT
mysqldump --defaults-extra-file="$MYSQL_DEFAULTS_FILE" \
  --single-transaction --quick --set-gtid-purged=OFF \
  --routines --events --triggers --hex-blob --no-tablespaces \
  --default-character-set=utf8mb4 "$DB_NAME" | gzip -9 > "$temporary"
gzip -t "$temporary"
test -s "$temporary"
mv -- "$temporary" "$output"
sha256sum "$output" > "$output.sha256"
chmod 0600 "$output" "$output.sha256"
echo "Backup created: $output"
echo "Verify with ops/mysql/verify-backup.sh; test restoring into an isolated database."
