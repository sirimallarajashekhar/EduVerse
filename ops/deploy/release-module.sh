#!/usr/bin/env bash
# Explicit operator-run deployment from an independently built, SHA-verified release archive.
# Usage: sudo bash ops/deploy/release-module.sh api /secure/incoming/api-<sha>.tar.gz
set -Eeuo pipefail
umask 027

if [[ ${EUID} -ne 0 ]]; then
  echo 'Run as root via sudo; systemd restart and release directory writes require privileges.' >&2
  exit 1
fi
if [[ $# -ne 2 ]]; then
  echo 'Usage: release-module.sh {api|portal|hrms|ams} /absolute/path/module-<sha>.tar.gz' >&2
  exit 2
fi
module="$1"
archive="$2"
case "$module" in
  api) service=eduverse-api; health='http://127.0.0.1:5201/health/ready' ;;
  portal) service=eduverse-portal; health='http://127.0.0.1:5200/' ;;
  hrms) service=eduverse-hrms; health='http://127.0.0.1:5203/hrms/health/live' ;;
  ams) service=eduverse-ams; health='http://127.0.0.1:5202/ams/health/live' ;;
  *) echo 'Unsupported module.' >&2; exit 2 ;;
esac
if [[ "$archive" != /* || ! -f "$archive" || ! -f "$archive.sha256" ]]; then
  echo 'Absolute release archive and adjacent .sha256 file are required.' >&2
  exit 2
fi
if ! command -v sha256sum >/dev/null || ! command -v tar >/dev/null ||
   ! command -v curl >/dev/null || ! command -v systemctl >/dev/null; then
  echo 'Missing sha256sum, tar, curl or systemctl.' >&2
  exit 2
fi
# The checksum manifest must reference the file from its original artifact bundle.
( cd "$(dirname "$archive")" && sha256sum -c "$(basename "$archive").sha256" )
name="$(basename "$archive")"
if [[ ! "$name" =~ ^${module}-([0-9a-f]{40})\.tar\.gz$ ]]; then
  echo 'Archive must include the full immutable Git SHA.' >&2
  exit 2
fi
sha="${BASH_REMATCH[1]}"
root="/opt/eduverse/$module"
releases="$root/releases"
target="$releases/$sha"
install -d -m 0750 -o root -g eduverse "$root" "$releases"
# Fail closed on unexpected archive paths, links, or non-regular archive members.
while IFS= read -r member; do
  case "$member" in
    '.'|'./') ;;
    './'*)
      sub="${member#./}"
      if [[ "$sub" == /* || "$sub" == *'../'* || "$sub" == '..' || "$sub" == */.. ]]; then
        echo "Unsafe archive entry: $member" >&2; exit 1
      fi ;;
    *) echo "Non-relative archive entry: $member" >&2; exit 1 ;;
  esac
done < <(tar -tzf "$archive")
if tar -tvzf "$archive" | awk 'substr($0,1,1) !~ /[-d]/ {bad=1} END {exit !bad}'; then
  echo 'Release bundle contains unsafe links or special files.' >&2
  exit 1
fi
if [[ -e "$target" ]]; then
  echo 'This immutable SHA has already been deployed; refusing overwrite.' >&2
  exit 1
fi
temporary="$(mktemp -d "$releases/.staging.XXXXXXXX")"
old=''
if [[ -L "$root/current" ]]; then
  old="$(readlink "$root/current")"
fi
cleanup() {
  if [[ -d "$temporary" ]]; then rm -rf -- "$temporary"; fi
}
trap cleanup EXIT
tar -xzf "$archive" --no-same-owner --no-same-permissions -C "$temporary"
if [[ ! -s "$temporary/release-info.txt" ]] ||
   ! grep -Fxq "module=$module" "$temporary/release-info.txt" ||
   ! grep -Fxq "commit=$sha" "$temporary/release-info.txt"; then
  echo 'Release metadata does not match the signed module/commit identifier.' >&2
  exit 1
fi
# Never include runtime secrets or production appsettings in an artifact.
mv -- "$temporary" "$target"
chgrp -R eduverse "$target"
chmod -R g+rX "$target"
temporary=''
ln -s "$target" "$root/current.new"
mv -Tf "$root/current.new" "$root/current"
if ! systemctl restart "$service" || ! curl --fail --silent --show-error --max-time 15 --retry 5 --retry-delay 3 "$health" >/dev/null; then
  echo 'New release failed service restart/readiness probe.' >&2
  if [[ -n "$old" ]]; then
    ln -s "$old" "$root/current.rollback"
    mv -Tf "$root/current.rollback" "$root/current"
    systemctl restart "$service" || true
  else
    rm -f -- "$root/current"
    systemctl stop "$service" || true
  fi
  exit 1
fi
echo "Deployed $module commit $sha. Health probe passed."
