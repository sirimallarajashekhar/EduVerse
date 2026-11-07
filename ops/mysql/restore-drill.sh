#!/usr/bin/env bash
# Manual isolated restore drill. NEVER touches the source database.
# Usage: MYSQL_DEFAULTS_FILE=/secure/restore.cnf bash ops/mysql/restore-drill.sh /absolute/backup.sql.gz eduverse_restore_20261107
# Requires a dedicated restore account allowed to create/use only a sandbox schema.
set -Eeuo pipefail
umask 077

if [[ $# != 2 ]]; then
  echo 'Usage: restore-drill.sh /absolute/source.sql.gz eduverse_restore_<identifier>' >&2
  exit 2
fi
archive="$1"
target="$2"
: "${MYSQL_DEFAULTS_FILE:?Provide a protected MySQL restore-client option file}"
if [[ "$archive" != /* || "$MYSQL_DEFAULTS_FILE" != /* || ! -f "$MYSQL_DEFAULTS_FILE" ||
      ! -f "$archive" || ! -f "$archive.sha256" ]]; then
  echo 'Archive, checksum and MySQL option file must exist at absolute paths.' >&2
  exit 2
fi
if [[ -L "$archive" || -L "$archive.sha256" || -L "$MYSQL_DEFAULTS_FILE" ]]; then
  echo 'Symlinked backups or credentials are not accepted.' >&2
  exit 2
fi
if [[ ! "$target" =~ ^eduverse_restore_[a-z0-9_]{4,40}$ ]]; then
  echo 'The target must be a dedicated eduverse_restore_... schema.' >&2
  exit 2
fi
permissions="$(stat -c '%a' "$MYSQL_DEFAULTS_FILE")"
if (( (8#$permissions & 8#077) != 0 )); then
  echo 'MySQL option file grants group/other permissions.' >&2
  exit 1
fi
for cmd in mysql gzip sha256sum grep; do
  command -v "$cmd" >/dev/null 2>&1 || { echo "Missing $cmd." >&2; exit 2; }
done
( cd "$(dirname "$archive")" && sha256sum -c "$(basename "$archive").sha256" )
gzip -t "$archive"
# Only create a new, empty sandbox. Never overwrite an existing schema.
exists="$(mysql --defaults-extra-file="$MYSQL_DEFAULTS_FILE" --batch --skip-column-names \
  --execute="SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name='$target'")"
if [[ "$exists" != 0 ]]; then
  echo 'Target schema already exists. Choose another empty sandbox name.' >&2
  exit 1
fi
mysql --defaults-extra-file="$MYSQL_DEFAULTS_FILE" \
  --execute="CREATE DATABASE \`$target\` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci"
echo 'Created isolated restore schema.'
if ! gzip -cd "$archive" | mysql --defaults-extra-file="$MYSQL_DEFAULTS_FILE" "$target"; then
  echo 'Restore import failed; sandbox remains for investigation. Never retry over the same schema.' >&2
  exit 1
fi
schema="$(mysql --defaults-extra-file="$MYSQL_DEFAULTS_FILE" --batch --skip-column-names "$target" \
  --execute="SELECT COUNT(*) FROM schema_migrations WHERE version='015_cross_module_workflows'")"
tables="$(mysql --defaults-extra-file="$MYSQL_DEFAULTS_FILE" --batch --skip-column-names \
  --execute="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='$target'")"
if [[ "$schema" != 1 || ! "$tables" =~ ^[0-9]+$ || "$tables" -lt 30 ]]; then
  echo 'Restore contents failed expected schema/table checks; preserve sandbox for review.' >&2
  exit 1
fi
echo "Isolated restore verified: $target, $tables tables, migration 015 present."
echo 'Record UTC timestamp, elapsed restore time, table counts and operator approval in the pilot evidence log.'
echo "Restore sandbox is intentionally preserved for audit; remove it manually after review."
