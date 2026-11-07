#!/usr/bin/env bash
# Read-only backup integrity check. Does not import or overwrite any database.
set -Eeuo pipefail
if [[ $# -ne 1 || "$1" != /* || ! -f "$1" || ! -f "$1.sha256" ]]; then
  echo 'Usage: verify-backup.sh /absolute/path/name.sql.gz' >&2
  exit 2
fi
archive="$1"
[[ "$archive" == *.sql.gz ]] || { echo 'Expected .sql.gz backup.' >&2; exit 2; }
(cd "$(dirname "$archive")" && sha256sum -c "$(basename "$archive").sha256")
gzip -t "$archive"
# A text-level sanity check is not a substitute for an isolated restore drill.
if ! gzip -cd "$archive" | grep -m1 -Eq '^-- MySQL dump|^-- MariaDB dump'; then
  echo 'Missing expected dump header.' >&2; exit 1
fi
echo 'Checksum, compression integrity and header verification passed.'
