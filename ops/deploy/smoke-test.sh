#!/usr/bin/env bash
# Read-only smoke probes for an already configured Linux host. No login or user data.
# API_URL=https://api.example.org PORTAL_URL=https://erp.example.org \
# HRMS_URL=https://erp.example.org AMS_URL=https://erp.example.org bash ops/deploy/smoke-test.sh
set -Eeuo pipefail
: "${API_URL:?Set API_URL}"
: "${PORTAL_URL:?Set PORTAL_URL}"
: "${HRMS_URL:?Set HRMS_URL}"
: "${AMS_URL:?Set AMS_URL}"
probe() {
  local label="$1" base="$2" path="$3"
  if [[ ! "$base" =~ ^https://[a-zA-Z0-9.-]+(:[0-9]{1,5})?$ &&
        ! "$base" =~ ^http://127[.]0[.]0[.]1:[0-9]{1,5}$ ]]; then
    echo "Non-TLS public URL rejected for $label." >&2
    exit 2
  fi
  local status
  status="$(curl --silent --show-error --max-time 15 --retry 3 --retry-delay 2 \
    --output /dev/null --write-out '%{http_code}' "${base%/}$path")"
  if [[ ! "$status" =~ ^2[0-9]{2}$ ]]; then
    printf 'Unexpected status %s on %s %s\n' "$status" "$label" "$path" >&2
    return 1
  fi
  printf 'OK %s %s\n' "$label" "$path"
}
probe 'API live' "$API_URL" '/health/live'
probe 'API ready' "$API_URL" '/health/ready'
probe 'Portal' "$PORTAL_URL" '/'
probe 'HRMS' "$HRMS_URL" '/hrms/health/live'
probe 'AMS' "$AMS_URL" '/ams/health/live'
echo 'Public smoke probes passed; protected workflows still require separate authorized pilot tests.'
