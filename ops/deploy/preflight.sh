#!/usr/bin/env bash
# Operator-run checks before deploying EduVerse to a Linux host.
# Does not print credentials, test external connectivity, or make changes.
set -Eeuo pipefail
umask 077

error=0
fail() { printf 'FAIL: %s\n' "$1" >&2; error=1; }
pass() { printf 'PASS: %s\n' "$1"; }
need() { if command -v "$1" >/dev/null 2>&1; then pass "$1 installed"; else fail "$1 missing"; fi; }

if [[ "${EUID}" -ne 0 ]]; then
    echo 'Run as root to inspect protected environment files.' >&2
    exit 2
fi

for cmd in dotnet systemctl curl sha256sum tar mysql mysqldump gzip; do need "$cmd"; done

if command -v dotnet >/dev/null 2>&1; then
    if dotnet --list-runtimes | grep -Eq '^Microsoft.AspNetCore.App 10\.'; then
        pass 'ASP.NET Core 10 runtime available'
    else
        fail 'ASP.NET Core 10 runtime not installed'
    fi
fi

if id eduverse >/dev/null 2>&1; then pass 'unprivileged eduverse account exists'; else fail 'eduverse account missing'; fi

for module in api portal hrms ams; do
    unit="eduverse-$module.service"
    if systemctl cat "$unit" >/dev/null 2>&1; then
        pass "$unit installed"
    else
        fail "$unit not installed"
    fi
    path="/etc/eduverse/env/$module.env"
    if [[ ! -f "$path" || -L "$path" ]]; then
        fail "$module environment file absent or symlinked"
        continue
    fi
    perms="$(stat -c '%a' "$path")"
    owner="$(stat -c '%u' "$path")"
    if [[ "$perms" == '600' && "$owner" == '0' ]]; then
        pass "$module env is root-owned, mode 0600"
    else
        fail "$module env must be root-owned, mode 0600"
    fi
    # Check only presence of keys; do not print values or source untrusted shell assignments.
    required='Api__BaseUrl Auth__KeyRingPath'
    if [[ "$module" == api ]]; then
        required='ConnectionStrings__EduVerse Authentication__Issuer Authentication__Audience Authentication__SigningKey'
    fi
    for key in $required; do
        if grep -Eq "^$key=.+$" "$path"; then
            pass "$module setting $key present"
        else
            fail "$module setting $key missing or empty"
        fi
    done
done

keyring="/var/lib/eduverse/keyring"
if [[ -d "$keyring" && ! -L "$keyring" ]]; then
    if [[ "$(stat -c '%U' "$keyring")" == eduverse &&
          "$(stat -c '%a' "$keyring")" == '700' ]]; then
        pass 'shared SSO key ring exists and is owned by eduverse with mode 0700'
    else
        fail 'key ring must be owned by eduverse with mode 0700'
    fi
else
    fail 'persistent shared SSO key ring is missing'
fi

backup="/etc/eduverse/mysql-backup.cnf"
if [[ -f "$backup" && ! -L "$backup" &&
      "$(stat -c '%u' "$backup")" == 0 &&
      "$(stat -c '%a' "$backup")" == '600' ]]; then
    pass 'dedicated MySQL backup account option file is protected'
else
    fail 'MySQL backup account option file missing or not root-owned mode 0600'
fi

if [[ "$error" != 0 ]]; then
    echo 'Preflight failed; do not deploy or enable pilot use.' >&2
    exit 1
fi
echo 'Local configuration preflight passed. TLS, restores, schema, tenant isolation and backups still require independent verification.'
